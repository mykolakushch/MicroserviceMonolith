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
    /// Supports dead-letter threshold to eliminate poison message loops and automatic cleanup to prevent memory leaks.
    /// </summary>
    public class InMemoryOutboxStore : IOutboxStore
    {
        private readonly ConcurrentDictionary<Guid, OutboxMessage> _messages = new();

        /// <inheritdoc />
        public int MaxRetries { get; set; } = 5;

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
            // Exclude already processed messages and poison messages that exceeded MaxRetries
            var pending = _messages.Values
                .Where(m => !m.IsProcessed && m.RetryCount < MaxRetries)
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
                // Clean up processed messages immediately to prevent unbounded memory leaks
                _messages.TryRemove(messageId, out _);
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

        /// <inheritdoc />
        public Task ClearProcessedAsync(CancellationToken cancellationToken = default)
        {
            var processedIds = _messages.Values.Where(m => m.IsProcessed).Select(m => m.Id).ToList();
            foreach (var id in processedIds)
            {
                _messages.TryRemove(id, out _);
            }
            return Task.CompletedTask;
        }
    }
}
