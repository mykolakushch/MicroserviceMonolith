using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Context;
using MicroserviceMonolith.Core.Execution;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// High-performance, thread-safe in-memory event bus supporting publish/subscribe,
    /// pipeline middlewares, sequential or parallel dispatch, and Dependency Injection resolution.
    /// Separates event publication pipeline from individual handler pipelines to preserve subscriber idempotency.
    /// </summary>
    public class EventBus : IEventPublisher, IEventSubscriber
    {
        private readonly IServiceProvider? _serviceProvider;
        private readonly ConcurrentDictionary<Type, List<Func<object, CancellationToken, Task>>> _handlers = new();
        private readonly List<IEventMiddleware> _publishMiddlewares = new();
        private readonly List<IEventHandlerMiddleware> _handlerMiddlewares = new();
        private readonly object _middlewareLock = new();
        private readonly object _subscribersLock = new();

        /// <summary>
        /// Initializes a new instance of <see cref="EventBus"/> for manual subscription.
        /// </summary>
        public EventBus() : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="EventBus"/> with dependency injection support.
        /// </summary>
        /// <param name="serviceProvider">Optional service provider to resolve event handlers and middlewares.</param>
        public EventBus(IServiceProvider? serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Adds a middleware component to the event publication pipeline (or handler pipeline if it implements <see cref="IEventHandlerMiddleware"/>).
        /// </summary>
        public void AddMiddleware(IEventMiddleware middleware)
        {
            if (middleware == null) throw new ArgumentNullException(nameof(middleware));
            lock (_middlewareLock)
            {
                if (middleware is IEventHandlerMiddleware handlerMiddleware)
                {
                    _handlerMiddlewares.Add(handlerMiddleware);
                }
                else
                {
                    _publishMiddlewares.Add(middleware);
                }
            }
        }

        /// <summary>
        /// Adds a middleware component that intercepts each subscriber individually (e.g. per-subscriber retry).
        /// </summary>
        public void AddHandlerMiddleware(IEventHandlerMiddleware middleware)
        {
            if (middleware == null) throw new ArgumentNullException(nameof(middleware));
            lock (_middlewareLock)
            {
                _handlerMiddlewares.Add(middleware);
            }
        }

        /// <inheritdoc />
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class
        {
            return PublishAsync(@event, EventPublishStrategy.Sequential, cancellationToken);
        }

        /// <inheritdoc />
        public async Task PublishAsync<TEvent>(
            TEvent @event,
            EventPublishStrategy strategy,
            CancellationToken cancellationToken = default)
            where TEvent : class
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));

            cancellationToken.ThrowIfCancellationRequested();

            if (strategy == EventPublishStrategy.Background)
            {
                var sp = _serviceProvider;
                var correlationAccessor = sp?.GetService<ICorrelationContextAccessor>();
                var ambientCorrelation = correlationAccessor?.CorrelationContext;
                var queue = sp?.GetService<ExecutionQueue>();

                if (queue != null)
                {
                    // Dispatch via high-performance ExecutionQueue
                    queue.Enqueue(async ct =>
                    {
                        if (ambientCorrelation != null && correlationAccessor != null)
                        {
                            correlationAccessor.CorrelationContext = ambientCorrelation;
                        }
                        await PublishCoreAsync(@event, EventPublishStrategy.Sequential, ct);
                    });
                }
                else
                {
                    // Fallback to Task.Run with error logging
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (ambientCorrelation != null && correlationAccessor != null)
                            {
                                correlationAccessor.CorrelationContext = ambientCorrelation;
                            }
                            await PublishCoreAsync(@event, EventPublishStrategy.Sequential, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            var logger = sp?.GetService<ILogger>();
                            logger?.Log($"[EventBus Background Error] Failed to publish {@event.GetType().Name}: {ex.Message}");
                        }
                    }, CancellationToken.None);
                }

                return;
            }

            await PublishCoreAsync(@event, strategy, cancellationToken);
        }

        private async Task PublishCoreAsync<TEvent>(
            TEvent @event,
            EventPublishStrategy strategy,
            CancellationToken cancellationToken)
            where TEvent : class
        {
            var publishMiddlewares = new List<IEventMiddleware>();
            var handlerMiddlewares = new List<IEventHandlerMiddleware>();

            lock (_middlewareLock)
            {
                publishMiddlewares.AddRange(_publishMiddlewares);
                handlerMiddlewares.AddRange(_handlerMiddlewares);
            }

            IServiceScope? scope = null;
            try
            {
                IServiceProvider? sp = _serviceProvider;
                if (sp != null)
                {
                    scope = sp.CreateScope();
                    sp = scope.ServiceProvider;

                    // Resolve DI middlewares
                    var diEventMiddlewares = sp.GetServices<IEventMiddleware>();
                    foreach (var m in diEventMiddlewares)
                    {
                        if (m is IEventHandlerMiddleware hm)
                        {
                            if (!handlerMiddlewares.Contains(hm)) handlerMiddlewares.Add(hm);
                        }
                        else
                        {
                            if (!publishMiddlewares.Contains(m)) publishMiddlewares.Add(m);
                        }
                    }

                    var diHandlerMiddlewares = sp.GetServices<IEventHandlerMiddleware>();
                    foreach (var hm in diHandlerMiddlewares)
                    {
                        if (!handlerMiddlewares.Contains(hm)) handlerMiddlewares.Add(hm);
                    }
                }

                // Core dispatch delegate
                Func<Task> dispatchAction = async () =>
                {
                    var rawHandlers = new List<Func<CancellationToken, Task>>();

                    // 1. Snapshot manual subscribers thread-safely
                    if (_handlers.TryGetValue(typeof(TEvent), out var list))
                    {
                        Func<object, CancellationToken, Task>[] snapshot;
                        lock (_subscribersLock)
                        {
                            snapshot = list.ToArray();
                        }
                        foreach (var handler in snapshot)
                        {
                            rawHandlers.Add(ct => handler(@event, ct));
                        }
                    }

                    // 2. Resolve DI event handlers
                    if (sp != null)
                    {
                        var diHandlers = sp.GetServices<IEventHandler<TEvent>>();
                        foreach (var diHandler in diHandlers)
                        {
                            rawHandlers.Add(ct => diHandler.HandleAsync(@event, ct));
                        }
                    }

                    if (rawHandlers.Count == 0)
                    {
                        return;
                    }

                    // Wrap each handler with per-handler middlewares (preserves idempotency on retries!)
                    var decoratedHandlers = new List<Func<CancellationToken, Task>>();
                    foreach (var rawHandler in rawHandlers)
                    {
                        Func<CancellationToken, Task> decorated = ct =>
                        {
                            Func<Task> pipeline = () => rawHandler(ct);
                            for (int i = handlerMiddlewares.Count - 1; i >= 0; i--)
                            {
                                var hm = handlerMiddlewares[i];
                                var next = pipeline;
                                pipeline = () => hm.InvokeAsync(@event, next, ct);
                            }
                            return pipeline();
                        };
                        decoratedHandlers.Add(decorated);
                    }

                    if (strategy == EventPublishStrategy.Parallel)
                    {
                        var tasks = decoratedHandlers.Select(h => h(cancellationToken));
                        await Task.WhenAll(tasks);
                    }
                    else
                    {
                        var exceptions = new List<Exception>();
                        foreach (var handler in decoratedHandlers)
                        {
                            try
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                await handler(cancellationToken);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                exceptions.Add(ex);
                            }
                        }

                        if (exceptions.Count == 1)
                        {
                            throw exceptions[0];
                        }
                        if (exceptions.Count > 1)
                        {
                            throw new AggregateException($"Multiple exceptions occurred while publishing event '{typeof(TEvent).Name}'.", exceptions);
                        }
                    }
                };

                // Build overall publication middleware pipeline
                Func<Task> pipeline = dispatchAction;

                for (int i = publishMiddlewares.Count - 1; i >= 0; i--)
                {
                    var middleware = publishMiddlewares[i];
                    var next = pipeline;
                    pipeline = () => middleware.InvokeAsync(@event, next, cancellationToken);
                }

                await pipeline();
            }
            finally
            {
                scope?.Dispose();
            }
        }

        /// <inheritdoc />
        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var list = _handlers.GetOrAdd(typeof(TEvent), _ => new List<Func<object, CancellationToken, Task>>());

            Func<object, CancellationToken, Task> wrapped = (evt, ct) => handler((TEvent)evt, ct);

            lock (_subscribersLock)
            {
                list.Add(wrapped);
            }

            return new Subscription(() =>
            {
                lock (_subscribersLock)
                {
                    list.Remove(wrapped);
                }
            });
        }

        /// <inheritdoc />
        public IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : class
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return Subscribe<TEvent>((evt, _) => handler(evt));
        }

        private sealed class Subscription : IDisposable
        {
            private Action? _unsubscribe;

            public Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;

            public void Dispose()
            {
                var action = Interlocked.Exchange(ref _unsubscribe, null);
                action?.Invoke();
            }
        }
    }
}
