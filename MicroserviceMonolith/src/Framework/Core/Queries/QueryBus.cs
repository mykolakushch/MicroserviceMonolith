using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace MicroserviceMonolith.Core.Queries
{
    /// <summary>
    /// In-memory query bus implementing dispatching of queries to their respective handlers.
    /// Supports manual registration as well as resolution via <see cref="IServiceProvider"/>.
    /// </summary>
    public class QueryBus : IQueryBus
    {
        private readonly IServiceProvider? _serviceProvider;
        private readonly ConcurrentDictionary<Type, Func<object, CancellationToken, Task<object?>>> _handlers = new();
        private readonly List<IQueryMiddleware> _middlewares = new();
        private readonly object _middlewareLock = new();

        public QueryBus() : this(null)
        {
        }

        public QueryBus(IServiceProvider? serviceProvider)
        {
            _serviceProvider = serviceProvider;
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
        public async Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            var queryType = query.GetType();
            var method = typeof(QueryBus)
                .GetMethods()
                .First(m => m.Name == nameof(QueryAsync) && m.IsGenericMethod && m.GetGenericArguments().Length == 2)
                .MakeGenericMethod(queryType, typeof(TResult));

            var task = (Task<TResult>)method.Invoke(this, new object[] { query, cancellationToken })!;
            return await task;
        }

        /// <inheritdoc />
        public async Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken = default) where TQuery : IQuery<TResult>
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            cancellationToken.ThrowIfCancellationRequested();

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
                    var handlerType = typeof(IQueryHandler<TQuery, TResult>);
                    var handler = sp.GetService(handlerType) as IQueryHandler<TQuery, TResult>;
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
    }
}
