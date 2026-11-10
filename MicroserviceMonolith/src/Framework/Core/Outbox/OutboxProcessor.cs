using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Context;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Outbox
{
    /// <summary>
    /// Contract for outbox message background processing.
    /// </summary>
    public interface IOutboxProcessor
    {
        /// <summary>
        /// Processes a single batch of pending outbox messages and dispatches them via <see cref="IEventPublisher"/>.
        /// </summary>
        /// <param name="batchSize">Number of messages to process.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The number of messages successfully processed.</returns>
        Task<int> ProcessBatchAsync(int batchSize = 50, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Default processor for dispatching outbox messages.
    /// Caches a typed publisher per event type (reflection runs once per event type, not per message)
    /// and restores ambient <see cref="CorrelationContext"/> for end-to-end tracing.
    /// </summary>
    /// <remarks>
    /// Delivery is at-least-once: if publishing succeeds but the result cannot be recorded (or one of several
    /// subscribers fails), the message is delivered again, so subscribers must be idempotent.
    /// When constructed with an <see cref="IServiceScopeFactory"/>, a new scope is created for every batch and the
    /// <see cref="IOutboxStore"/> is resolved from it, which allows scoped (for example database-backed) stores.
    /// </remarks>
    public class OutboxProcessor : IOutboxProcessor
    {
        private readonly IOutboxStore? _outboxStore;
        private readonly IServiceScopeFactory? _scopeFactory;
        private readonly IEventPublisher _eventPublisher;
        private readonly ICorrelationContextAccessor? _correlationAccessor;
        private readonly Action<string>? _logAction;
        private readonly ILogger? _logger;

        private static readonly ConcurrentDictionary<Type, PublishInvoker> _publishInvokers = new();

        /// <summary>
        /// Publishes an event known only as <see cref="object"/> through the typed <see cref="IEventPublisher"/> API.
        /// </summary>
        private abstract class PublishInvoker
        {
            public abstract Task InvokeAsync(IEventPublisher publisher, object @event, CancellationToken ct);
        }

        private sealed class PublishInvoker<TEvent> : PublishInvoker where TEvent : class
        {
            public override Task InvokeAsync(IEventPublisher publisher, object @event, CancellationToken ct)
                => publisher.PublishAsync((TEvent)@event, ct);
        }

        public OutboxProcessor(IOutboxStore outboxStore, IEventPublisher eventPublisher, ILogger? logger = null)
            : this(outboxStore, eventPublisher, null, logger)
        {
        }

        /// <summary>
        /// Creates a processor that logs through the framework <see cref="ILogger"/> (failures and dead-lettering
        /// are written with <see cref="LogSeverity.Error"/>).
        /// </summary>
        public OutboxProcessor(
            IOutboxStore outboxStore,
            IEventPublisher eventPublisher,
            ICorrelationContextAccessor? correlationAccessor,
            ILogger? logger)
            : this(outboxStore, eventPublisher, correlationAccessor, (Action<string>?)null)
        {
            _logger = logger;
        }

        public OutboxProcessor(
            IOutboxStore outboxStore,
            IEventPublisher eventPublisher,
            ICorrelationContextAccessor? correlationAccessor,
            Action<string>? logAction = null)
        {
            _outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
            _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
            _correlationAccessor = correlationAccessor;
            _logAction = logAction;
        }

        /// <summary>
        /// Creates a processor that resolves <see cref="IOutboxStore"/> from a new scope for every batch,
        /// so the store may be registered with a scoped lifetime.
        /// </summary>
        public OutboxProcessor(
            IServiceScopeFactory scopeFactory,
            IEventPublisher eventPublisher,
            ICorrelationContextAccessor? correlationAccessor = null,
            Action<string>? logAction = null)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
            _correlationAccessor = correlationAccessor;
            _logAction = logAction;
        }

        /// <summary>
        /// Creates a processor that resolves <see cref="IOutboxStore"/> from a new scope for every batch and logs
        /// through the framework <see cref="ILogger"/>.
        /// </summary>
        public OutboxProcessor(
            IServiceScopeFactory scopeFactory,
            IEventPublisher eventPublisher,
            ICorrelationContextAccessor? correlationAccessor,
            ILogger? logger)
            : this(scopeFactory, eventPublisher, correlationAccessor, (Action<string>?)null)
        {
            _logger = logger;
        }

        private void LogInfo(string message)
        {
            if (_logger != null) _logger.Log(message);
            else _logAction?.Invoke(message);
        }

        private void LogError(string message)
        {
            if (_logger != null) _logger.Log(LogSeverity.Error, message);
            else _logAction?.Invoke(message);
        }

        /// <inheritdoc />
        public async Task<int> ProcessBatchAsync(int batchSize = 50, CancellationToken cancellationToken = default)
        {
            IServiceScope? scope = null;
            try
            {
                var store = _outboxStore;
                if (store == null)
                {
                    scope = _scopeFactory!.CreateScope();
                    store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
                }

                return await ProcessBatchCoreAsync(store, batchSize, cancellationToken);
            }
            finally
            {
                scope?.Dispose();
            }
        }

        private async Task<int> ProcessBatchCoreAsync(IOutboxStore store, int batchSize, CancellationToken cancellationToken)
        {
            var pendingMessages = await store.GetPendingMessagesAsync(batchSize, cancellationToken);
            int processedCount = 0;
            var previousCorrelationContext = _correlationAccessor?.CorrelationContext;

            try
            {
                foreach (var message in pendingMessages)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // Restore the message's correlation context (or clear the previous message's one)
                        if (_correlationAccessor != null)
                        {
                            _correlationAccessor.CorrelationContext = string.IsNullOrWhiteSpace(message.CorrelationId)
                                ? null
                                : new CorrelationContext(
                                    message.CorrelationId,
                                    causationId: message.CausationId ?? message.Id.ToString());
                        }

                        var eventObj = message.Event;
                        var eventType = eventObj.GetType();

                        // Typed invoker is created once per event type (reflection), then called without reflection
                        var publishInvoker = _publishInvokers.GetOrAdd(eventType, t =>
                            (PublishInvoker)Activator.CreateInstance(typeof(PublishInvoker<>).MakeGenericType(t))!);

                        await publishInvoker.InvokeAsync(_eventPublisher, eventObj, cancellationToken);

                        await store.MarkAsProcessedAsync(message.Id, cancellationToken);
                        processedCount++;
                        LogInfo($"[Outbox] Processed message {message.Id} of type {message.EventType}");
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
                    {
                        // Cancellation requested by the caller is not a delivery failure and must not consume a retry.
                        var attempts = message.RetryCount + 1;
                        LogError($"[Outbox] Failed to process message {message.Id} (attempt {attempts}): {ex}");
                        await store.MarkAsFailedAsync(message.Id, ex.Message, cancellationToken);

                        if (attempts >= store.MaxRetries)
                        {
                            LogError($"[Outbox] Message {message.Id} of type {message.EventType} exhausted {store.MaxRetries} attempts and is dead-lettered");
                        }
                    }
                }

                return processedCount;
            }
            finally
            {
                if (_correlationAccessor != null)
                {
                    _correlationAccessor.CorrelationContext = previousCorrelationContext;
                }
            }
        }
    }
}