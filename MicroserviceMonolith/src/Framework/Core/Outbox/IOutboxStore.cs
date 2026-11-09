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
        /// Saves a new outbox message.
        /// </summary>
        Task SaveAsync(OutboxMessage message, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves pending unprocessed messages.
        /// </summary>
        Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int batchSize = 50, CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks a message as successfully processed.
        /// </summary>
        Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks a message as failed with an error message and increments its retry count.
        /// </summary>
        Task MarkAsFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default);
    }
}
