using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace MicroserviceMonolith.Core.Context
{
    /// <summary>
    /// Represents the ambient correlation context for request/message tracing across module boundaries.
    /// </summary>
    public class CorrelationContext
    {
        /// <summary>
        /// Unique identifier for the overall logical operation or transaction.
        /// </summary>
        public string CorrelationId { get; set; }

        /// <summary>
        /// Identifier of the direct cause (previous message or operation) that triggered this execution.
        /// </summary>
        public string? CausationId { get; set; }

        /// <summary>
        /// Optional user or identity identifier.
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>
        /// W3C distributed trace identifier if available.
        /// </summary>
        public string? TraceId { get; set; }

        /// <summary>
        /// Timestamp when this context was initialized.
        /// </summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>
        /// Additional metadata or headers passed along with the execution.
        /// </summary>
        public IDictionary<string, object?> Items { get; }

        public CorrelationContext()
            : this(Guid.NewGuid().ToString("N"))
        {
        }

        public CorrelationContext(string correlationId, string? causationId = null, string? userId = null, string? traceId = null)
        {
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId;
            CausationId = causationId;
            UserId = userId;
            TraceId = traceId;
            Timestamp = DateTimeOffset.UtcNow;
            Items = new ConcurrentDictionary<string, object?>();
        }
    }
}
