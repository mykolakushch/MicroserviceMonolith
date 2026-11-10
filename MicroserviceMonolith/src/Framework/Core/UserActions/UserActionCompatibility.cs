using System;
using MicroserviceMonolith.Core.Events;

namespace MicroserviceMonolith.Core.UserActions
{
    /// <summary>
    /// Compatibility shim. <c>UserAction</c> is an adapter, not a core concept, and moved to
    /// <see cref="MicroserviceMonolith.Adapters.UserActions"/>. It no longer derives from <c>ModuleBase</c>.
    /// </summary>
    [Obsolete("Moved to MicroserviceMonolith.Adapters.UserActions.UserAction. This shim will be removed in 2.0.")]
    public class UserAction : MicroserviceMonolith.Adapters.UserActions.UserAction
    {
        public UserAction(IEventPublisher eventPublisher) : base(eventPublisher)
        {
        }
    }

    /// <summary>
    /// Compatibility shim, see <see cref="UserAction"/>.
    /// </summary>
    [Obsolete("Moved to MicroserviceMonolith.Adapters.UserActions.UserActionContext. This shim will be removed in 2.0.")]
    public record UserActionContext : MicroserviceMonolith.Adapters.UserActions.UserActionContext
    {
        public UserActionContext(object sender, object args, string? correlationId = null)
            : base(sender, args, correlationId)
        {
        }
    }

    /// <summary>
    /// Compatibility shim, see <see cref="UserAction"/>.
    /// </summary>
    [Obsolete("Moved to MicroserviceMonolith.Adapters.UserActions.UserActionContext<TArgs>. This shim will be removed in 2.0.")]
    public record UserActionContext<TArgs> : MicroserviceMonolith.Adapters.UserActions.UserActionContext<TArgs>
    {
        public UserActionContext(object sender, TArgs args, string? correlationId = null)
            : base(sender, args, correlationId)
        {
        }
    }
}
