using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace MicroserviceMonolith.Core.Context
{
    /// <summary>
    /// The per-dispatch setup shared by <c>CommandBus</c>, <c>QueryBus</c> and <c>EventBus</c>: the DI scope
    /// (own or the shared ambient one) and the correlation context. Disposing the frame undoes everything it did:
    /// the scope it created is disposed, the ambient scope and a correlation context it created are restored.
    /// </summary>
    internal readonly struct DispatchFrame : IAsyncDisposable
    {
        private readonly IServiceScope? _scope;
        private readonly bool _ambientSet;
        private readonly AmbientScopeEntry? _previousAmbient;
        private readonly ICorrelationContextAccessor? _correlationAccessor;
        private readonly bool _correlationCreated;

        private DispatchFrame(
            IServiceProvider? services,
            IServiceScope? scope,
            bool ambientSet,
            AmbientScopeEntry? previousAmbient,
            ICorrelationContextAccessor? correlationAccessor,
            bool correlationCreated)
        {
            Services = services;
            _scope = scope;
            _ambientSet = ambientSet;
            _previousAmbient = previousAmbient;
            _correlationAccessor = correlationAccessor;
            _correlationCreated = correlationCreated;
        }

        /// <summary>
        /// The provider to resolve handlers and middlewares from; null when the bus has no service provider.
        /// </summary>
        public IServiceProvider? Services { get; }

        /// <param name="root">The bus's service provider (the owner of any ambient scope).</param>
        /// <param name="shareScope">Reuse the ambient scope of an outer dispatch and publish this one as ambient.</param>
        /// <param name="correlationAccessor">Accessor of the correlation context, if any.</param>
        /// <param name="ensureCorrelation">Create a correlation context when none is present (restored on dispose).</param>
        public static DispatchFrame Begin(
            IServiceProvider? root,
            bool shareScope,
            ICorrelationContextAccessor? correlationAccessor,
            bool ensureCorrelation)
        {
            var correlationCreated = false;
            if (ensureCorrelation && correlationAccessor != null && correlationAccessor.CorrelationContext == null)
            {
                correlationAccessor.CorrelationContext = new CorrelationContext();
                correlationCreated = true;
            }

            if (root == null)
            {
                return new DispatchFrame(null, null, false, null, correlationAccessor, correlationCreated);
            }

            if (shareScope && AmbientDispatchScope.TryGet(root, out var shared))
            {
                // Nested dispatch: reuse the scope of the outermost one, which owns and disposes it
                return new DispatchFrame(shared, null, false, null, correlationAccessor, correlationCreated);
            }

            var scope = root.CreateScope();
            AmbientScopeEntry? previous = null;
            if (shareScope)
            {
                previous = AmbientDispatchScope.Set(root, scope.ServiceProvider);
            }

            return new DispatchFrame(scope.ServiceProvider, scope, shareScope, previous, correlationAccessor, correlationCreated);
        }

        public async ValueTask DisposeAsync()
        {
            if (_ambientSet)
            {
                AmbientDispatchScope.Restore(_previousAmbient);
            }

            if (_correlationCreated && _correlationAccessor != null)
            {
                _correlationAccessor.CorrelationContext = null;
            }

            await ScopeDisposal.DisposeAsync(_scope).ConfigureAwait(false);
        }
    }

    internal static class ScopeDisposal
    {
        /// <summary>
        /// Disposes a scope asynchronously when it supports it: a synchronous <c>Dispose</c> throws if the scope
        /// holds a service that only implements <see cref="IAsyncDisposable"/>.
        /// </summary>
        public static async ValueTask DisposeAsync(IServiceScope? scope)
        {
            if (scope is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                scope?.Dispose();
            }
        }
    }
}
