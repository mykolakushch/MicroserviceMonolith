using System;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// Represents an event in the system indicating that something significant has happened.
    /// Events are past-tense, immutable facts.
    /// </summary>
    public interface IEvent
    {
        /// <summary>
        /// Unique identifier for this event occurrence.
        /// </summary>
        Guid EventId { get; }

        /// <summary>
        /// Timestamp when the event occurred in UTC.
        /// </summary>
        DateTimeOffset OccurredOn { get; }
    }

    /// <summary>
    /// Base record implementation of <see cref="IEvent"/>.
    /// </summary>
    public abstract record EventBase : IEvent
    {
        /// <inheritdoc />
        public Guid EventId { get; init; } = Guid.NewGuid();

        /// <inheritdoc />
        public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
    }
}
