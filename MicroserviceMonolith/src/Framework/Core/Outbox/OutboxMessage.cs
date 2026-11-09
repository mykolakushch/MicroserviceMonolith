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

        public bool IsProcessed => ProcessedOn.HasValue;
    }
}
