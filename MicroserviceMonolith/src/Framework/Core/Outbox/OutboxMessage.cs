using System;
using System.Collections.Generic;

namespace MicroserviceMonolith.Core.Outbox
{
    /// <summary>
    /// Represents an event message stored in the transactional outbox.
    /// </summary>
    public class OutboxMessage
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string EventType { get; set; } = string.Empty;

        /// <summary>
        /// The event instance. Always set for messages created in this process; <c>null</c> for a message loaded
        /// from a durable store, which is recreated from <see cref="Payload"/> by an <see cref="IOutboxEventSerializer"/>.
        /// </summary>
        public object? Event { get; init; }

        /// <summary>
        /// Serialized event (see <see cref="IOutboxEventSerializer"/>). Together with <see cref="EventType"/> and
        /// <see cref="SchemaVersion"/> this is what a durable store persists.
        /// </summary>
        public string? Payload { get; set; }

        /// <summary>
        /// Version of the event schema that <see cref="Payload"/> was written with. Default: 1.
        /// </summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>
        /// Free-form metadata for transports and stores (for example routing keys).
        /// </summary>
        public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
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
