using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Events
{
    public class EventBus : IEventPublisher, IEventSubscriber
    {
        private readonly ConcurrentDictionary<Type, List<Func<object, Task>>> _handlers = new();
        private readonly List<IEventMiddleware> _middlewares = new();

        public void AddMiddleware(IEventMiddleware middleware)
        {
            _middlewares.Add(middleware);
        }

        public async Task PublishAsync<TEvent>(TEvent @event) where TEvent : class
        {
            if (_handlers.TryGetValue(typeof(TEvent), out var handlers))
            {
                foreach (var handler in handlers)
                {
                    Func<Task> pipeline = () => handler(@event);
                    foreach (var middleware in _middlewares.AsEnumerable().Reverse())
                    {
                        var next = pipeline;
                        pipeline = () => middleware.InvokeAsync(@event, next);
                    }
                    await pipeline();
                }
            }
        }

        public IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : class
        {
            var handlers = _handlers.GetOrAdd(typeof(TEvent), _ => new List<Func<object, Task>>());
            Func<object, Task> wrapped = (evt) => handler((TEvent)evt);
            handlers.Add(wrapped);
            return new Subscription(() => handlers.Remove(wrapped));
        }

        private class Subscription : IDisposable
        {
            private readonly Action _unsubscribe;
            public Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;
            public void Dispose() => _unsubscribe();
        }
    }
}
