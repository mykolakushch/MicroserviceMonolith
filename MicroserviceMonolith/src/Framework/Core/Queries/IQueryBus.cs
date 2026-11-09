using System.Threading;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Queries
{
    /// <summary>
    /// Defines a contract for dispatching queries to their corresponding query handlers.
    /// </summary>
    public interface IQueryBus
    {
        /// <summary>
        /// Asynchronously sends a query to its registered handler and returns the result.
        /// </summary>
        /// <typeparam name="TResult">The expected result type.</typeparam>
        /// <param name="query">The query to execute.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task containing the query result.</returns>
        Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously sends a typed query to its registered handler.
        /// </summary>
        /// <typeparam name="TQuery">The specific query type.</typeparam>
        /// <typeparam name="TResult">The expected result type.</typeparam>
        /// <param name="query">The query to execute.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task containing the query result.</returns>
        Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken = default) where TQuery : IQuery<TResult>;
    }
}
