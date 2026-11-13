using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Outbox
{
    /// <summary>
    /// Storage contract for saving, querying, and updating transactional outbox messages.
    /// </summary>
    public interface IOutboxStore
    {
        /// <summary>
        /// Maximum number of delivery retry attempts before a message is marked as dead-letter.
        /// </summary>
        int MaxRetries { get; set; }

        /// <summary>
        /// Saves a new outbox message.
        /// </summary>
        Task SaveAsync(OutboxMessage message, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves pending unprocessed messages that have not exceeded <see cref="MaxRetries"/>.
        /// </summary>
        /// <remarks>
        /// Implementations should not hand the same message to two concurrent processors (for example by leasing
        /// it, or with <c>SELECT ... FOR UPDATE SKIP LOCKED</c>), and should honor retry backoff
        /// (<see cref="OutboxMessage.NextAttemptOn"/>). A lease that expires without a reported result should count as a
        /// failed attempt (increment <see cref="OutboxMessage.RetryCount"/>), otherwise a message that crashes its
        /// processor is never dead-lettered. The in-memory store does all of this.
        /// </remarks>
        Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int batchSize = 50, CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks a message as successfully processed.
        /// </summary>
        Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks a message as failed with an error message and increments its retry count.
        /// </summary>
        Task MarkAsFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears all processed messages from storage to maintain flat memory usage.
        /// </summary>
        Task ClearProcessedAsync(CancellationToken cancellationToken = default);
    }
}
