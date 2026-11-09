using System;
using System.Threading;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// Middleware that intercepts event publishing in the event pipeline.
    /// </summary>
    public interface IEventMiddleware
    {
        /// <summary>
        /// Asynchronously intercepts and processes the given event before and after handlers are invoked.
        /// </summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <param name="event">The event instance.</param>
        /// <param name="next">Delegate to invoke the rest of the pipeline.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task representing the operation.</returns>
        Task InvokeAsync<TEvent>(
            TEvent @event,
            Func<Task> next,
            CancellationToken cancellationToken = default)
            where TEvent : class;
    }
}
