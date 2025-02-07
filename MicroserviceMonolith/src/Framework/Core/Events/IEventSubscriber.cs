using System;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// Defines a contract for event subscribers. The subscriber listens for events and handles them when they are published.
    /// </summary>
    public interface IEventSubscriber
    {
        /// <summary>
        /// Subscribes to an event and registers a handler to process the event when it is published.
        /// </summary>
        /// <typeparam name="TEvent">The type of event to subscribe to. It must be a class.</typeparam>
        /// <param name="handler">The handler function to process the event when it is published. This function receives the event instance as its parameter.</param>
        /// <returns>A disposable object that can be used to unsubscribe from the event. Once disposed, the handler will no longer be invoked for this event.</returns>
        IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : class;
    }
}
