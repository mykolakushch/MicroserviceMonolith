using System.Threading;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// Contract for publishing domain or integration events to subscribed modules.
    /// </summary>
    public interface IEventPublisher
    {
        /// <summary>
        /// Publishes an event to all subscribers using the default sequential strategy.
        /// </summary>
        /// <typeparam name="TEvent">The type of event.</typeparam>
        /// <param name="event">The event instance.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task representing the publish operation.</returns>
        Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class;

        /// <summary>
        /// Publishes an event to all subscribers using a specified execution strategy.
        /// </summary>
        /// <typeparam name="TEvent">The type of event.</typeparam>
        /// <param name="event">The event instance.</param>
        /// <param name="strategy">The dispatch strategy (Sequential or Parallel).</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task representing the publish operation.</returns>
        Task PublishAsync<TEvent>(TEvent @event, EventPublishStrategy strategy, CancellationToken cancellationToken = default) where TEvent : class;
    }
}
