using System;
using System.Threading;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Context;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Queries
{
    /// <summary>
    /// Middleware that logs the execution of queries with correlation tracking.
    /// </summary>
    public class LoggingQueryMiddleware : IQueryMiddleware
    {
        private readonly Action<string> _logAction;
        private readonly ICorrelationContextAccessor? _correlationAccessor;

        public LoggingQueryMiddleware(ILogger logger, ICorrelationContextAccessor? correlationAccessor = null)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logAction = logger.Log;
            _correlationAccessor = correlationAccessor;
        }

        public LoggingQueryMiddleware(
            Microsoft.Extensions.Logging.ILogger<LoggingQueryMiddleware> logger,
            ICorrelationContextAccessor? correlationAccessor = null)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logAction = msg => Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "{Message}", msg);
            _correlationAccessor = correlationAccessor;
        }

        public LoggingQueryMiddleware(Action<string> logAction, ICorrelationContextAccessor? correlationAccessor = null)
        {
            _logAction = logAction ?? throw new ArgumentNullException(nameof(logAction));
            _correlationAccessor = correlationAccessor;
        }

        /// <inheritdoc />
        public async Task<TResult> InvokeAsync<TQuery, TResult>(
            TQuery query,
            Func<Task<TResult>> next,
            CancellationToken cancellationToken = default)
            where TQuery : IQuery<TResult>
        {
            var queryName = query.GetType().Name;
            var cid = _correlationAccessor?.CorrelationContext?.CorrelationId;
            var prefix = !string.IsNullOrWhiteSpace(cid) ? $"[CID: {cid}] " : string.Empty;

            _logAction($"{prefix}[Query] Start {queryName}");
            try
            {
                var result = await next();
                _logAction($"{prefix}[Query] End {queryName}");
                return result;
            }
            catch (Exception ex)
            {
                _logAction($"{prefix}[Query] Failed {queryName}: {ex}");
                throw;
            }
        }
    }
}
