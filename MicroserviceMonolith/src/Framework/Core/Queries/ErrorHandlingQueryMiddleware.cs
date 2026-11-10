using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MicroserviceMonolith.Core.Queries
{
    /// <summary>
    /// Middleware that logs unhandled errors during query execution.
    /// </summary>
    public class ErrorHandlingQueryMiddleware : IQueryMiddleware
    {
        private readonly Action<string, Exception> _logErrorAction;

        public ErrorHandlingQueryMiddleware(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logErrorAction = (msg, ex) => logger.LogError(ex, "[Query] {Message}", msg);
        }

        public ErrorHandlingQueryMiddleware(Action<string, Exception> logErrorAction)
        {
            _logErrorAction = logErrorAction ?? throw new ArgumentNullException(nameof(logErrorAction));
        }

        /// <inheritdoc />
        public async Task<TResult> InvokeAsync<TQuery, TResult>(
            TQuery query,
            Func<Task<TResult>> next,
            CancellationToken cancellationToken = default)
            where TQuery : IQuery<TResult>
        {
            try
            {
                return await next();
            }
            catch (Exception ex)
            {
                _logErrorAction($"Error in query {query.GetType().Name}", ex);
                throw;
            }
        }
    }
}
