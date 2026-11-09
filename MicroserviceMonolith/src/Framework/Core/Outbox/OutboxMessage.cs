using System;

namespace MicroserviceMonolith.Core.Outbox
{
    /// <summary>
    /// Represents an event message stored in the transactional outbox.
    /// </summary>
    public class OutboxMessage
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string EventType { get; init; } = string.Empty;
        public object Event { get; init; } = default!;
        public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? ProcessedOn { get; set; }
        public int RetryCount { get; set; }
        public string? LastError { get; set; }
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }

        /// <summary>
        /// While set to a future time, the message is leased by a processor and is not handed out again.
        /// Lets several processors share one store without dispatching the same message twice.
        /// </summary>
        public DateTimeOffset? LockedUntil { get; set; }

        /// <summary>
        /// Earliest time at which a failed message may be attempted again (retry backoff).
        /// </summary>
        public DateTimeOffset? NextAttemptOn { get; set; }

        public bool IsProcessed => ProcessedOn.HasValue;
        public bool IsDeadLetter(int maxRetries) => !IsProcessed && RetryCount >= maxRetries;
    }
}
