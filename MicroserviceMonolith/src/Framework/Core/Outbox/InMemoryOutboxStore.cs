using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Outbox
{
    /// <summary>
    /// Thread-safe in-memory implementation of <see cref="IOutboxStore"/>.
    /// </summary>
    public class InMemoryOutboxStore : IOutboxStore
    {
        private readonly ConcurrentDictionary<Guid, OutboxMessage> _messages = new();

        /// <inheritdoc />
        public Task SaveAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            _messages[message.Id] = message;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int batchSize = 50, CancellationToken cancellationToken = default)
        {
            var pending = _messages.Values
                .Where(m => !m.IsProcessed)
                .OrderBy(m => m.OccurredOn)
                .Take(batchSize)
                .ToList();

            return Task.FromResult<IReadOnlyList<OutboxMessage>>(pending);
        }

        /// <inheritdoc />
        public Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            if (_messages.TryGetValue(messageId, out var message))
            {
                message.ProcessedOn = DateTimeOffset.UtcNow;
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task MarkAsFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default)
        {
            if (_messages.TryGetValue(messageId, out var message))
            {
                message.RetryCount++;
                message.LastError = error;
            }
            return Task.CompletedTask;
        }
    }
}
