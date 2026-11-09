using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Context;

namespace MicroserviceMonolith.Core.Queries
{
    /// <summary>
    /// In-memory query bus implementing dispatching of queries to their respective handlers.
    /// Supports manual registration as well as resolution via <see cref="IServiceProvider"/>.
    /// Dispatch targets are cached per query type, so reflection runs only once per query type (when its cache
    /// entry is created) and not on every call. Not trimming/Native AOT safe.
    /// </summary>
    public class QueryBus : IQueryBus
    {
        private readonly IServiceProvider? _serviceProvider;
        private readonly ICorrelationContextAccessor? _correlationAccessor;
        private readonly ConcurrentDictionary<Type, Func<object, CancellationToken, Task<object?>>> _handlers = new();
        private readonly List<IQueryMiddleware> _middlewares = new();
        private readonly object _middlewareLock = new();

        public QueryBus() : this(null, null)
        {
        }

        public QueryBus(IServiceProvider? serviceProvider) : this(serviceProvider, serviceProvider?.GetService<ICorrelationContextAccessor>())
        {
        }

        public QueryBus(IServiceProvider? serviceProvider, ICorrelationContextAccessor? correlationAccessor)
        {
            _serviceProvider = serviceProvider;
            _correlationAccessor = correlationAccessor;
        }

        /// <summary>
        /// Registers a handler for a specific query type manually.
        /// </summary>
        public void RegisterHandler<TQuery, TResult>(IQueryHandler<TQuery, TResult> handler) where TQuery : IQuery<TResult>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _handlers[typeof(TQuery)] = async (q, ct) =>
            {
                var result = await handler.HandleAsync((TQuery)q, ct);
                return (object?)result;
            };
        }

        /// <summary>
        /// Adds a middleware component to the query bus pipeline.
        /// </summary>
        public void AddMiddleware(IQueryMiddleware middleware)
        {
            if (middleware == null) throw new ArgumentNullException(nameof(middleware));
            lock (_middlewareLock)
            {
                _middlewares.Add(middleware);
            }
        }

        /// <inheritdoc />
        public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            return QuerySenderCache<TResult>.QueryAsync(this, query, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken = default) where TQuery : IQuery<TResult>
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            cancellationToken.ThrowIfCancellationRequested();

            // Auto-initialize correlation context if missing
            if (_correlationAccessor != null && _correlationAccessor.CorrelationContext == null)
            {
                _correlationAccessor.CorrelationContext = new CorrelationContext();
            }

            var allMiddlewares = new List<IQueryMiddleware>();
            lock (_middlewareLock)
            {
                allMiddlewares.AddRange(_middlewares);
            }

            IServiceScope? scope = null;
            try
            {
                IServiceProvider? sp = _serviceProvider;
                if (sp != null)
                {
                    scope = sp.CreateScope();
                    sp = scope.ServiceProvider;
                    allMiddlewares.AddRange(sp.GetServices<IQueryMiddleware>());
                }

                Func<CancellationToken, Task<TResult>> handlerFunc;

                if (_handlers.TryGetValue(typeof(TQuery), out var manualHandler))
                {
                    handlerFunc = async ct =>
                    {
                        var res = await manualHandler(query, ct);
                        return (TResult)res!;
                    };
                }
                else if (sp != null)
                {
                    var handler = sp.GetService<IQueryHandler<TQuery, TResult>>();
                    if (handler != null)
                    {
                        handlerFunc = ct => handler.HandleAsync(query, ct);
                    }
                    else
                    {
                        throw new InvalidOperationException($"No query handler registered for query '{typeof(TQuery).FullName}'.");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"No query handler registered for query '{typeof(TQuery).FullName}'.");
                }

                // Build middleware pipeline
                Func<Task<TResult>> pipeline = () => handlerFunc(cancellationToken);

                for (int i = allMiddlewares.Count - 1; i >= 0; i--)
                {
                    var middleware = allMiddlewares[i];
                    var next = pipeline;
                    pipeline = () => middleware.InvokeAsync(query, next, cancellationToken);
                }

                return await pipeline();
            }
            finally
            {
                scope?.Dispose();
            }
        }

        /// <summary>
        /// Calls the typed <c>QueryAsync&lt;TQuery, TResult&gt;</c> for a query known only through <see cref="IQuery{TResult}"/>.
        /// </summary>
        private abstract class QueryInvokerBase<TResult>
        {
            public abstract Task<TResult> InvokeAsync(QueryBus bus, IQuery<TResult> query, CancellationToken ct);
        }

        private sealed class QueryInvoker<TQuery, TResult> : QueryInvokerBase<TResult> where TQuery : IQuery<TResult>
        {
            public override Task<TResult> InvokeAsync(QueryBus bus, IQuery<TResult> query, CancellationToken ct)
                => bus.QueryAsync<TQuery, TResult>((TQuery)query, ct);
        }

        /// <summary>
        /// Per-result-type cache of invokers keyed by the runtime query type. Reflection is used once per query
        /// type to create the invoker; every later call is a plain virtual call.
        /// </summary>
        private static class QuerySenderCache<TResult>
        {
            private static readonly ConcurrentDictionary<Type, QueryInvokerBase<TResult>> _invokers = new();

            public static Task<TResult> QueryAsync(QueryBus bus, IQuery<TResult> query, CancellationToken ct)
            {
                var invoker = _invokers.GetOrAdd(query.GetType(), qType =>
                    (QueryInvokerBase<TResult>)Activator.CreateInstance(
                        typeof(QueryInvoker<,>).MakeGenericType(qType, typeof(TResult)))!);

                return invoker.InvokeAsync(bus, query, ct);
            }
        }
    }
}
