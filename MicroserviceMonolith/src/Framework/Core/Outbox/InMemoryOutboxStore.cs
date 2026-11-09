using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Outbox
{
    /// <summary>
    /// Thread-safe in-memory implementation of <see cref="IOutboxStore"/>. It is a reference implementation:
    /// messages are lost when the process exits and writes are not part of any business transaction.
    /// For durable, transactional delivery implement <see cref="IOutboxStore"/> over your database.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>Messages handed out by <see cref="GetPendingMessagesAsync"/> are leased for <see cref="LeaseDuration"/>, so concurrent processors do not dispatch the same message twice.</description></item>
    /// <item><description>A failed message is retried with exponential backoff (<see cref="RetryBaseDelay"/>, capped by <see cref="RetryMaxDelay"/>).</description></item>
    /// <item><description>A message that reached <see cref="MaxRetries"/> is a dead letter: it is no longer delivered, stays inspectable via <see cref="GetDeadLetters"/> and can be removed with <see cref="PurgeDeadLetters"/>.</description></item>
    /// </list>
    /// </remarks>
    public class InMemoryOutboxStore : IOutboxStore
    {
        private readonly ConcurrentDictionary<Guid, OutboxMessage> _messages = new();
        private readonly object _stateLock = new();
        private readonly TimeProvider _timeProvider;

        public InMemoryOutboxStore() : this(TimeProvider.System)
        {
        }

        public InMemoryOutboxStore(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        /// <inheritdoc />
        public int MaxRetries { get; set; } = 5;

        /// <summary>
        /// How long a message handed out for processing stays leased. If the processor does not report a result
        /// within this time (for example it crashed), the message becomes available again. Default: 30 seconds.
        /// </summary>
        public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Delay before the first retry of a failed message; it doubles with every further failure.
        /// <see cref="TimeSpan.Zero"/> disables the backoff. Default: 1 second.
        /// </summary>
        public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Upper bound of the retry delay. Default: 5 minutes.
        /// </summary>
        public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(5);

        /// <inheritdoc />
        public Task SaveAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            _messages[message.Id] = message;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Returned messages are leased (see <see cref="LeaseDuration"/>); they are not returned again until the
        /// lease expires or the processor reports a result through <see cref="MarkAsProcessedAsync"/> or <see cref="MarkAsFailedAsync"/>.
        /// </remarks>
        public Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int batchSize = 50, CancellationToken cancellationToken = default)
        {
            lock (_stateLock)
            {
                var now = _timeProvider.GetUtcNow();

                // Exclude processed messages, dead letters, messages waiting for their retry time and leased messages
                var batch = _messages.Values
                    .Where(m => !m.IsProcessed
                                && m.RetryCount < MaxRetries
                                && (m.NextAttemptOn == null || m.NextAttemptOn <= now)
                                && (m.LockedUntil == null || m.LockedUntil <= now))
                    .OrderBy(m => m.OccurredOn)
                    .Take(batchSize)
                    .ToList();

                var leaseUntil = now + LeaseDuration;
                foreach (var message in batch)
                {
                    message.LockedUntil = leaseUntil;
                }

                return Task.FromResult<IReadOnlyList<OutboxMessage>>(batch);
            }
        }

        /// <inheritdoc />
        public Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            lock (_stateLock)
            {
                if (_messages.TryGetValue(messageId, out var message))
                {
                    message.ProcessedOn = _timeProvider.GetUtcNow();
                    message.LockedUntil = null;
                    // Clean up processed messages immediately to prevent unbounded memory growth
                    _messages.TryRemove(messageId, out _);
                }
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task MarkAsFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default)
        {
            lock (_stateLock)
            {
                if (_messages.TryGetValue(messageId, out var message))
                {
                    message.RetryCount++;
                    message.LastError = error;
                    message.LockedUntil = null;
                    message.NextAttemptOn = ComputeNextAttempt(message.RetryCount, _timeProvider.GetUtcNow());
                }
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

        /// <summary>
        /// Returns the messages that exhausted <see cref="MaxRetries"/> and will not be delivered any more,
        /// oldest first. Dead letters stay in memory until <see cref="PurgeDeadLetters"/> is called.
        /// </summary>
        public IReadOnlyList<OutboxMessage> GetDeadLetters()
        {
            lock (_stateLock)
            {
                return _messages.Values
                    .Where(m => m.IsDeadLetter(MaxRetries))
                    .OrderBy(m => m.OccurredOn)
                    .ToList();
            }
        }

        /// <summary>
        /// Removes all dead letters from memory.
        /// </summary>
        /// <returns>The number of removed messages.</returns>
        public int PurgeDeadLetters()
        {
            lock (_stateLock)
            {
                var ids = _messages.Values.Where(m => m.IsDeadLetter(MaxRetries)).Select(m => m.Id).ToList();
                foreach (var id in ids)
                {
                    _messages.TryRemove(id, out _);
                }
                return ids.Count;
            }
        }

        private DateTimeOffset ComputeNextAttempt(int retryCount, DateTimeOffset now)
        {
            if (RetryBaseDelay <= TimeSpan.Zero)
            {
                return now;
            }

            var exponent = Math.Min(retryCount - 1, 30);
            var delayMs = RetryBaseDelay.TotalMilliseconds * Math.Pow(2, exponent);
            delayMs = Math.Min(delayMs, RetryMaxDelay.TotalMilliseconds);
            return now.AddMilliseconds(delayMs);
        }
    }
}
