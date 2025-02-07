using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// An event bus that allows publishing and subscribing to events.
    /// It handles event propagation and supports middleware for additional processing.
    /// </summary>
    public class EventBus : IEventPublisher, IEventSubscriber
    {
        // A dictionary that stores event handlers for different event types.
        // The key is the event type, and the value is a list of handlers (functions that process the event).
        private readonly ConcurrentDictionary<Type, List<Func<object, Task>>> _handlers = new();

        // A list that holds middleware instances used during event processing.
        private readonly List<IEventMiddleware> _middlewares = new();

        /// <summary>
        /// Adds a middleware to the event bus.
        /// The middleware will be executed in the event processing pipeline.
        /// </summary>
        /// <param name="middleware">The middleware instance to be added to the pipeline.</param>
        public void AddMiddleware(IEventMiddleware middleware)
        {
            // Adds the middleware to the list of middlewares.
            _middlewares.Add(middleware);
        }

        /// <summary>
        /// Publishes an event asynchronously, triggering all handlers associated with the event type.
        /// The event will pass through the middleware pipeline before being handled.
        /// </summary>
        /// <typeparam name="TEvent">The type of the event being published. It must be a class.</typeparam>
        /// <param name="event">The event instance to be published.</param>
        /// <returns>A task representing the asynchronous operation of event publication.</returns>
        public async Task PublishAsync<TEvent>(TEvent @event) where TEvent : class
        {
            // Check if there are any handlers registered for the specified event type.
            if (_handlers.TryGetValue(typeof(TEvent), out var handlers))
            {
                // For each handler, create a pipeline with the middleware and the handler.
                foreach (var handler in handlers)
                {
                    // Initial pipeline with the handler.
                    Func<Task> pipeline = () => handler(@event);

                    // Reverse the middlewares to ensure they are applied in the correct order.
                    foreach (var middleware in _middlewares.AsEnumerable().Reverse())
                    {
                        // Each middleware is responsible for invoking the next step in the pipeline.
                        var next = pipeline;
                        pipeline = () => middleware.InvokeAsync(@event, next);
                    }

                    // Execute the pipeline, which will invoke the middleware and eventually the handler.
                    await pipeline();
                }
            }
        }

        /// <summary>
        /// Subscribes to an event of a specific type and provides a handler that processes the event.
        /// </summary>
        /// <typeparam name="TEvent">The type of the event to subscribe to. It must be a class.</typeparam>
        /// <param name="handler">A function that processes the event when it is published.</param>
        /// <returns>A disposable subscription object that can be used to unsubscribe from the event.</returns>
        public IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : class
        {
            // Retrieve or create a list of handlers for the specified event type.
            var handlers = _handlers.GetOrAdd(typeof(TEvent), _ => new List<Func<object, Task>>());

            // Wrap the handler to match the expected delegate type (Func<object, Task>).
            Func<object, Task> wrapped = (evt) => handler((TEvent)evt);

            // Add the wrapped handler to the list of handlers.
            handlers.Add(wrapped);

            // Return a subscription that allows for unsubscribing from the event.
            return new Subscription(() => handlers.Remove(wrapped));
        }

        // A private class that represents a subscription, allowing for unsubscription.
        private class Subscription : IDisposable
        {
            // Action to unsubscribe from the event.
            private readonly Action _unsubscribe;

            /// <summary>
            /// Initializes a new instance of the <see cref="Subscription"/> class.
            /// </summary>
            /// <param name="unsubscribe">The action that removes the handler from the list of subscribers.</param>
            public Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;

            /// <summary>
            /// Disposes of the subscription and removes the handler from the list of subscribers.
            /// </summary>
            public void Dispose() => _unsubscribe();
        }
    }
}
