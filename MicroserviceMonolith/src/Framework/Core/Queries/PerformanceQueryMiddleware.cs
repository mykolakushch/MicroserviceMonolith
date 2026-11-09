using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Queries
{
    /// <summary>
    /// Middleware that tracks query execution duration and logs warnings if execution exceeds a given threshold.
    /// </summary>
    public class PerformanceQueryMiddleware : IQueryMiddleware
    {
        private readonly TimeSpan _threshold;
        private readonly Action<string> _logAction;

        public PerformanceQueryMiddleware(TimeSpan threshold, ILogger logger)
        {
            _threshold = threshold;
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logAction = logger.Log;
        }

        public PerformanceQueryMiddleware(TimeSpan threshold, Action<string> logAction)
        {
            _threshold = threshold;
            _logAction = logAction ?? throw new ArgumentNullException(nameof(logAction));
        }

        /// <inheritdoc />
        public async Task<TResult> InvokeAsync<TQuery, TResult>(
            TQuery query,
            Func<Task<TResult>> next,
            CancellationToken cancellationToken = default)
            where TQuery : IQuery<TResult>
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                return await next();
            }
            finally
            {
                stopwatch.Stop();
                if (stopwatch.Elapsed > _threshold)
                {
                    _logAction($"[Query Performance Warning] {query.GetType().Name} took {stopwatch.ElapsedMilliseconds} ms (threshold: {_threshold.TotalMilliseconds} ms)");
                }
            }
        }
    }
}
