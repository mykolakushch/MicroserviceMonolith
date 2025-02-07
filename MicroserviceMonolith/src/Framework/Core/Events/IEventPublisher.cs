using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// Defines a contract for event publishing. The publisher is responsible for distributing events to subscribers.
    /// </summary>
    public interface IEventPublisher
    {
        /// <summary>
        /// Asynchronously publishes an event to all registered subscribers.
        /// </summary>
        /// <typeparam name="TEvent">The type of event to be published. It must be a class.</typeparam>
        /// <param name="event">The event instance to be published to the subscribers.</param>
        /// <returns>A task representing the asynchronous operation of publishing the event.</returns>
        Task PublishAsync<TEvent>(TEvent @event) where TEvent : class;
    }
}
