using System;
using System.Threading;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Context;
using Microsoft.Extensions.Logging;

namespace MicroserviceMonolith.Core.Queries
{
    /// <summary>
    /// Middleware that logs the execution of queries with correlation tracking.
    /// </summary>
    public class LoggingQueryMiddleware : IQueryMiddleware
    {
        private readonly Action<string> _logAction;
        private readonly Action<string> _failureAction;
        private readonly ICorrelationContextAccessor? _correlationAccessor;

        public LoggingQueryMiddleware(
            ILogger logger,
            ICorrelationContextAccessor? correlationAccessor = null)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logAction = msg => logger.LogInformation("{Message}", msg);
            _failureAction = msg => logger.LogError("{Message}", msg);
            _correlationAccessor = correlationAccessor;
        }

        public LoggingQueryMiddleware(Action<string> logAction, ICorrelationContextAccessor? correlationAccessor = null)
        {
            _logAction = logAction ?? throw new ArgumentNullException(nameof(logAction));
            _failureAction = _logAction;
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
                _failureAction($"{prefix}[Query] Failed {queryName}: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }
    }
}
