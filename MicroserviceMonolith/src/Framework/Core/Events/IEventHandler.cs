using System.Threading;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// Contract for handling events of type <typeparamref name="TEvent"/> via dependency injection.
    /// </summary>
    /// <typeparam name="TEvent">The type of event being handled.</typeparam>
    public interface IEventHandler<in TEvent> where TEvent : class
    {
        /// <summary>
        /// Asynchronously handles the event notification.
        /// </summary>
        /// <param name="event">The event instance.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
    }
}
