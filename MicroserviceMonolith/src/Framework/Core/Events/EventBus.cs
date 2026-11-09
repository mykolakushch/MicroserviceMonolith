using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// High-performance, thread-safe in-memory event bus supporting publish/subscribe,
    /// pipeline middlewares, sequential or parallel dispatch, and Dependency Injection resolution.
    /// </summary>
    public class EventBus : IEventPublisher, IEventSubscriber
    {
        private readonly IServiceProvider? _serviceProvider;
        private readonly ConcurrentDictionary<Type, List<Func<object, CancellationToken, Task>>> _handlers = new();
        private readonly List<IEventMiddleware> _middlewares = new();
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
        /// Adds a middleware component to the event pipeline.
        /// </summary>
        public void AddMiddleware(IEventMiddleware middleware)
        {
            if (middleware == null) throw new ArgumentNullException(nameof(middleware));
            lock (_middlewareLock)
            {
                _middlewares.Add(middleware);
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
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await PublishCoreAsync(@event, EventPublishStrategy.Sequential, CancellationToken.None);
                    }
                    catch
                    {
                        // Background dispatch error handling
                    }
                }, CancellationToken.None);
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
            var allMiddlewares = new List<IEventMiddleware>();
            lock (_middlewareLock)
            {
                allMiddlewares.AddRange(_middlewares);
            }

            IServiceScope? scope = null;
            try
            {
                IServiceProvider? sp = _serviceProvider;
                if (sp != null)
                {
                    scope = sp.CreateScope();
                    sp = scope.ServiceProvider;
                    allMiddlewares.AddRange(sp.GetServices<IEventMiddleware>());
                }

                // Core dispatch delegate
                Func<Task> dispatchAction = async () =>
                {
                    var handlersToExecute = new List<Func<CancellationToken, Task>>();

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
                            handlersToExecute.Add(ct => handler(@event, ct));
                        }
                    }

                    // 2. Resolve DI event handlers
                    if (sp != null)
                    {
                        var diHandlers = sp.GetServices<IEventHandler<TEvent>>();
                        foreach (var diHandler in diHandlers)
                        {
                            handlersToExecute.Add(ct => diHandler.HandleAsync(@event, ct));
                        }
                    }

                    if (handlersToExecute.Count == 0)
                    {
                        return;
                    }

                    if (strategy == EventPublishStrategy.Parallel)
                    {
                        var tasks = handlersToExecute.Select(h => h(cancellationToken));
                        await Task.WhenAll(tasks);
                    }
                    else
                    {
                        var exceptions = new List<Exception>();
                        foreach (var handler in handlersToExecute)
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

                // Build middleware pipeline around the publication
                Func<Task> pipeline = dispatchAction;

                for (int i = allMiddlewares.Count - 1; i >= 0; i--)
                {
                    var middleware = allMiddlewares[i];
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
