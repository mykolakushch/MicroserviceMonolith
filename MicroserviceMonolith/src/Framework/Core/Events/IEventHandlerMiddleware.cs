using System;
using System.Threading;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// Middleware that intercepts the execution of an individual event subscriber/handler.
    /// Allows retry policies, isolated error handling, and metrics to execute per-subscriber
    /// without re-executing already succeeded subscribers.
    /// </summary>
    public interface IEventHandlerMiddleware
    {
        /// <summary>
        /// Asynchronously intercepts and processes the given event for an individual handler.
        /// </summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <param name="event">The event instance.</param>
        /// <param name="next">Delegate to invoke the subscriber handler or next middleware.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task representing the operation.</returns>
        Task InvokeAsync<TEvent>(
            TEvent @event,
            Func<Task> next,
            CancellationToken cancellationToken = default)
            where TEvent : class;
    }
}
