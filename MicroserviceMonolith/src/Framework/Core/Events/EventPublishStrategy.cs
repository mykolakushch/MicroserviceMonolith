namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// Defines the dispatch strategy when delivering events to multiple subscribers.
    /// </summary>
    public enum EventPublishStrategy
    {
        /// <summary>
        /// Handlers are executed sequentially in subscription order.
        /// </summary>
        Sequential = 0,

        /// <summary>
        /// Handlers are executed concurrently in parallel using <see cref="System.Threading.Tasks.Task.WhenAll(System.Threading.Tasks.Task[])"/>.
        /// </summary>
        Parallel = 1,

        /// <summary>
        /// Delivery is scheduled in the background without blocking the publisher.
        /// </summary>
        Background = 2
    }
}
