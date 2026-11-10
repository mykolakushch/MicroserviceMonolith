using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// Middleware that tracks event publication duration and logs warnings if execution exceeds a given threshold.
    /// </summary>
    public class PerformanceEventMiddleware : IEventMiddleware
    {
        private readonly TimeSpan _threshold;
        private readonly Action<string> _logAction;

        public PerformanceEventMiddleware(TimeSpan threshold, ILogger logger)
        {
            _threshold = threshold;
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logAction = m => logger.LogWarning("{Message}", m);
        }

        public PerformanceEventMiddleware(TimeSpan threshold, Action<string> logAction)
        {
            _threshold = threshold;
            _logAction = logAction ?? throw new ArgumentNullException(nameof(logAction));
        }

        /// <inheritdoc />
        public async Task InvokeAsync<TEvent>(
            TEvent @event,
            Func<Task> next,
            CancellationToken cancellationToken = default)
            where TEvent : class
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await next();
            }
            finally
            {
                stopwatch.Stop();
                if (stopwatch.Elapsed > _threshold)
                {
                    _logAction($"[Event Performance Warning] {@event.GetType().Name} took {stopwatch.ElapsedMilliseconds} ms (threshold: {_threshold.TotalMilliseconds} ms)");
                }
            }
        }
    }
}
