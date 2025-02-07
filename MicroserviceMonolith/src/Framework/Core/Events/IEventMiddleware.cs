using System;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// Defines the contract for event middleware in the event processing pipeline.
    /// Middleware can perform actions before or after event handling, such as logging, error handling, or modifying the event.
    /// </summary>
    public interface IEventMiddleware
    {
        /// <summary>
        /// Asynchronously processes an event through the middleware.
        /// </summary>
        /// <typeparam name="TEvent">The type of event being processed. It must be a class.</typeparam>
        /// <param name="event">The event instance to be processed by the middleware.</param>
        /// <param name="next">The next function in the event handling pipeline, typically invoking the next middleware or the event handler.</param>
        /// <returns>A task representing the asynchronous operation of the event middleware.</returns>
        Task InvokeAsync<TEvent>(TEvent @event, Func<Task> next) where TEvent : class;
    }
}
