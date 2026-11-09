using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Context;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Execution
{
    /// <summary>
    /// Event arguments for errors that occur during background task execution in <see cref="ExecutionQueue"/>.
    /// </summary>
    public class ExecutionQueueErrorEventArgs : EventArgs
    {
        public Exception Exception { get; }

        public ExecutionQueueErrorEventArgs(Exception exception)
        {
            Exception = exception;
        }
    }

    /// <summary>
    /// High-performance asynchronous background execution queue backed by <see cref="Channel{T}"/>.
    /// Supports bounded backpressure, concurrent workers, graceful draining, cancellation, and error notifications.
    /// Captures and restores ambient <see cref="CorrelationContext"/> across asynchronous boundaries.
    /// </summary>
    public class ExecutionQueue : IDisposable, IAsyncDisposable
    {
        private readonly Channel<Func<CancellationToken, Task>> _channel;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<Task> _workers = new();
        private readonly ICorrelationContextAccessor? _correlationAccessor;
        private readonly Action<string, Exception>? _errorLogger;
        private readonly TimeSpan _drainTimeout;
        private int _isDisposed;

        /// <summary>
        /// Event raised whenever an enqueued task throws an unhandled exception.
        /// </summary>
        public event EventHandler<ExecutionQueueErrorEventArgs>? OnError;

        /// <summary>
        /// Initializes a new instance of <see cref="ExecutionQueue"/> with default options.
        /// </summary>
        public ExecutionQueue() : this(new ExecutionQueueOptions(), null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="ExecutionQueue"/> with an optional logger.
        /// </summary>
        public ExecutionQueue(ILogger? logger)
            : this(new ExecutionQueueOptions(), null, logger != null ? (msg, ex) => logger.Log($"[ExecutionQueue] {msg}: {ex.Message}") : null)
        {
        }

        /// <summary>
        /// Initializes a new instance with options and optional logger.
        /// </summary>
        public ExecutionQueue(ExecutionQueueOptions options, Action<string, Exception>? errorLogger = null)
            : this(options, null, errorLogger)
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="ExecutionQueue"/> with full options, correlation context accessor, and error logging.
        /// </summary>
        public ExecutionQueue(
            ExecutionQueueOptions options,
            ICorrelationContextAccessor? correlationAccessor,
            Action<string, Exception>? errorLogger = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.WorkerCount <= 0) throw new ArgumentOutOfRangeException(nameof(options.WorkerCount), "Worker count must be greater than 0.");

            _correlationAccessor = correlationAccessor;
            _errorLogger = errorLogger;
            _drainTimeout = options.DrainTimeout;

            if (options.Capacity.HasValue && options.Capacity.Value > 0)
            {
                var boundedOptions = new BoundedChannelOptions(options.Capacity.Value)
                {
                    FullMode = options.FullMode,
                    SingleReader = options.WorkerCount == 1,
                    SingleWriter = false
                };
                _channel = Channel.CreateBounded<Func<CancellationToken, Task>>(boundedOptions);
            }
            else
            {
                var unboundedOptions = new UnboundedChannelOptions
                {
                    SingleReader = options.WorkerCount == 1,
                    SingleWriter = false
                };
                _channel = Channel.CreateUnbounded<Func<CancellationToken, Task>>(unboundedOptions);
            }

            for (int i = 0; i < options.WorkerCount; i++)
            {
                _workers.Add(Task.Run(ProcessQueueAsync));
            }
        }

        /// <summary>
        /// Enqueues a task generator to be executed asynchronously in the background.
        /// </summary>
        public void Enqueue(Func<Task> taskGenerator)
        {
            if (taskGenerator == null) throw new ArgumentNullException(nameof(taskGenerator));
            Enqueue((_) => taskGenerator());
        }

        /// <summary>
        /// Enqueues a cancellation-aware task generator without blocking caller threads.
        /// </summary>
        public void Enqueue(Func<CancellationToken, Task> taskGenerator)
        {
            if (taskGenerator == null) throw new ArgumentNullException(nameof(taskGenerator));

            var wrappedTask = WrapWithCorrelation(taskGenerator);

            if (!_channel.Writer.TryWrite(wrappedTask))
            {
                // Non-blocking asynchronous schedule when bounded channel is full (avoids thread pool starvation)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _channel.Writer.WriteAsync(wrappedTask, _cts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _errorLogger?.Invoke("Failed to enqueue task into saturated queue", ex);
                    }
                }, _cts.Token);
            }
        }

        /// <summary>
        /// Asynchronously enqueues a task generator to be executed.
        /// </summary>
        public ValueTask EnqueueAsync(Func<CancellationToken, Task> taskGenerator, CancellationToken cancellationToken = default)
        {
            if (taskGenerator == null) throw new ArgumentNullException(nameof(taskGenerator));
            return _channel.Writer.WriteAsync(WrapWithCorrelation(taskGenerator), cancellationToken);
        }

        /// <summary>
        /// Attempts to enqueue a task generator immediately without waiting.
        /// </summary>
        public bool TryEnqueue(Func<CancellationToken, Task> taskGenerator)
        {
            if (taskGenerator == null) throw new ArgumentNullException(nameof(taskGenerator));
            return _channel.Writer.TryWrite(WrapWithCorrelation(taskGenerator));
        }

        private Func<CancellationToken, Task> WrapWithCorrelation(Func<CancellationToken, Task> taskGenerator)
        {
            var ambientContext = _correlationAccessor?.CorrelationContext;
            if (ambientContext == null)
            {
                return taskGenerator;
            }

            return async ct =>
            {
                if (_correlationAccessor != null)
                {
                    _correlationAccessor.CorrelationContext = ambientContext;
                }
                await taskGenerator(ct);
            };
        }

        private async Task ProcessQueueAsync()
        {
            var reader = _channel.Reader;
            while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var taskGenerator))
                {
                    try
                    {
                        await taskGenerator(_cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                    {
                        // Normal shutdown cancellation
                        break;
                    }
                    catch (Exception ex)
                    {
                        _errorLogger?.Invoke("Unhandled exception while executing queued task", ex);
                        try
                        {
                            OnError?.Invoke(this, new ExecutionQueueErrorEventArgs(ex));
                        }
                        catch
                        {
                            // Guard against handler errors
                        }
                    }
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            // Signal writer completion so reader processes existing items
            _channel.Writer.TryComplete();

            try
            {
                // Allow workers to finish existing tasks within drain timeout
                var drainTask = Task.WhenAll(_workers);
                if (await Task.WhenAny(drainTask, Task.Delay(_drainTimeout)).ConfigureAwait(false) != drainTask)
                {
                    // If timeout exceeded, cancel ongoing execution
                    _cts.Cancel();
                }
                await drainTask.ConfigureAwait(false);
            }
            catch
            {
                // Swallow cleanup cancellations
            }
            finally
            {
                _cts.Dispose();
            }
        }
    }
}
