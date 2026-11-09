using System;
using System.Threading;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Queries
{
    /// <summary>
    /// Middleware that logs the execution of queries.
    /// </summary>
    public class LoggingQueryMiddleware : IQueryMiddleware
    {
        private readonly Action<string> _logAction;

        public LoggingQueryMiddleware(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logAction = logger.Log;
        }

        public LoggingQueryMiddleware(Microsoft.Extensions.Logging.ILogger<LoggingQueryMiddleware> logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logAction = msg => Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "{Message}", msg);
        }

        public LoggingQueryMiddleware(Action<string> logAction)
        {
            _logAction = logAction ?? throw new ArgumentNullException(nameof(logAction));
        }

        /// <inheritdoc />
        public async Task<TResult> InvokeAsync<TQuery, TResult>(
            TQuery query,
            Func<Task<TResult>> next,
            CancellationToken cancellationToken = default)
            where TQuery : IQuery<TResult>
        {
            var queryName = query.GetType().Name;
            _logAction($"[Query] Start {queryName}");
            try
            {
                var result = await next();
                _logAction($"[Query] End {queryName}");
                return result;
            }
            catch (Exception ex)
            {
                _logAction($"[Query] Failed {queryName}: {ex.Message}");
                throw;
            }
        }
    }
}
