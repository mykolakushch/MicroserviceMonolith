using System.Threading;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Queries
{
    /// <summary>
    /// Defines a contract for handling queries of type <typeparamref name="TQuery"/> returning <typeparamref name="TResult"/>.
    /// </summary>
    /// <typeparam name="TQuery">The type of query to handle.</typeparam>
    /// <typeparam name="TResult">The type of result produced by the query.</typeparam>
    public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
    {
        /// <summary>
        /// Asynchronously executes the query and returns the result.
        /// </summary>
        /// <param name="query">The query instance to handle.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task representing the result of the query.</returns>
        Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
    }
}
