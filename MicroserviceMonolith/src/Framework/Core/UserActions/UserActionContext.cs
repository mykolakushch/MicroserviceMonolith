using System;
using System.Threading;

namespace MicroserviceMonolith.Core.UserActions
{
    /// <summary>
    /// Represents the contextual state and metadata of a user or system interaction triggering an action.
    /// </summary>
    public record UserActionContext
    {
        /// <summary>
        /// Source identifier or entity that initiated the action.
        /// </summary>
        public object Sender { get; init; }

        /// <summary>
        /// Payload or arguments associated with the action.
        /// </summary>
        public object Args { get; init; }

        /// <summary>
        /// Optional correlation identifier for distributed tracing.
        /// </summary>
        public string? CorrelationId { get; init; }

        public UserActionContext(object sender, object args, string? correlationId = null)
        {
            Sender = sender ?? throw new ArgumentNullException(nameof(sender));
            Args = args ?? throw new ArgumentNullException(nameof(args));
            CorrelationId = correlationId;
        }
    }

    /// <summary>
    /// Strongly-typed context for a user action.
    /// </summary>
    /// <typeparam name="TArgs">Type of payload arguments.</typeparam>
    public record UserActionContext<TArgs> : UserActionContext
    {
        public new TArgs Args => (TArgs)base.Args;

        public UserActionContext(object sender, TArgs args, string? correlationId = null)
            : base(sender, args!, correlationId)
        {
        }
    }
}
