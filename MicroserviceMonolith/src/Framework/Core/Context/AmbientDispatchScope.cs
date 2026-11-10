using System;
using System.Threading;

namespace MicroserviceMonolith.Core.Context
{
    internal sealed class AmbientScopeEntry
    {
        public AmbientScopeEntry(IServiceProvider owner, IServiceProvider scope)
        {
            Owner = owner;
            Scope = scope;
        }

        public IServiceProvider Owner { get; }

        public IServiceProvider Scope { get; }
    }

    /// <summary>
    /// Flows the DI scope of the outermost command/query/event dispatch to nested dispatches, so they can
    /// share scoped services (for example one unit of work). Only used when
    /// <c>ShareScopeBetweenNestedDispatches</c> is enabled. An entry is bound to the root provider that created
    /// it, so several containers in one process never see each other's scopes.
    /// </summary>
    internal static class AmbientDispatchScope
    {
        private static readonly AsyncLocal<AmbientScopeEntry?> Current = new();

        public static bool TryGet(IServiceProvider owner, out IServiceProvider scope)
        {
            var entry = Current.Value;
            if (entry != null && ReferenceEquals(entry.Owner, owner))
            {
                scope = entry.Scope;
                return true;
            }

            scope = null!;
            return false;
        }

        public static AmbientScopeEntry? Set(IServiceProvider owner, IServiceProvider scope)
        {
            var previous = Current.Value;
            Current.Value = new AmbientScopeEntry(owner, scope);
            return previous;
        }

        public static void Restore(AmbientScopeEntry? previous) => Current.Value = previous;

        /// <summary>
        /// Detaches the current flow from any ambient scope (used for background work that outlives the dispatch).
        /// </summary>
        public static void Clear() => Current.Value = null;
    }
}
