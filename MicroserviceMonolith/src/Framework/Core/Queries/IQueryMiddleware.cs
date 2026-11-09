using System;
using System.Threading;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Queries
{
    /// <summary>
    /// Middleware that intercepts query execution in the query processing pipeline.
    /// </summary>
    public interface IQueryMiddleware
    {
        /// <summary>
        /// Intercepts query execution, processes logic before and after, and invokes the next pipeline step.
        /// </summary>
        /// <typeparam name="TQuery">The query type.</typeparam>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="query">The query being executed.</param>
        /// <param name="next">Delegate to the next step in the pipeline.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task containing the query result.</returns>
        Task<TResult> InvokeAsync<TQuery, TResult>(
            TQuery query,
            Func<Task<TResult>> next,
            CancellationToken cancellationToken = default)
            where TQuery : IQuery<TResult>;
    }
}
