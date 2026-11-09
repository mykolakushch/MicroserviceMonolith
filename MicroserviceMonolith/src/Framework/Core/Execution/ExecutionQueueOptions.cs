using System;
using System.Threading.Channels;

namespace MicroserviceMonolith.Core.Execution
{
    /// <summary>
    /// Configuration options for the asynchronous <see cref="ExecutionQueue"/>.
    /// </summary>
    public class ExecutionQueueOptions
    {
        /// <summary>
        /// Optional maximum capacity for the channel. If null, the channel is unbounded.
        /// </summary>
        public int? Capacity { get; set; } = null;

        /// <summary>
        /// Behavior when the queue is bounded and full. Defaults to <see cref="BoundedChannelFullMode.Wait"/>.
        /// </summary>
        public BoundedChannelFullMode FullMode { get; set; } = BoundedChannelFullMode.Wait;

        /// <summary>
        /// Number of concurrent background worker tasks reading from the queue. Defaults to 1 (FIFO sequential).
        /// </summary>
        public int WorkerCount { get; set; } = 1;

        /// <summary>
        /// Maximum duration to wait for pending tasks to finish processing during graceful disposal. Defaults to 5 seconds.
        /// </summary>
        public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(5);
    }
}
