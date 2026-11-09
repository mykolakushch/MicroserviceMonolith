using System;
using System.Threading;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Queries
{
    /// <summary>
    /// Middleware that retries failed queries with exponential backoff and jitter.
    /// </summary>
    public class RetryQueryMiddleware : IQueryMiddleware
    {
        private readonly int _maxRetries;
        private readonly TimeSpan _initialDelay;
        private readonly double _backoffMultiplier;
        private readonly bool _useJitter;
        private readonly Func<Exception, bool>? _shouldRetry;
        private readonly Action<string> _logAction;
        private readonly Random _random = new();

        public RetryQueryMiddleware(int maxRetries, ILogger logger)
            : this(maxRetries, TimeSpan.FromMilliseconds(50), 2.0, true, null, logger.Log)
        {
        }

        public RetryQueryMiddleware(
            int maxRetries,
            TimeSpan initialDelay,
            double backoffMultiplier = 2.0,
            bool useJitter = true,
            Func<Exception, bool>? shouldRetry = null,
            Action<string>? logAction = null)
        {
            if (maxRetries < 0) throw new ArgumentOutOfRangeException(nameof(maxRetries));
            _maxRetries = maxRetries;
            _initialDelay = initialDelay < TimeSpan.Zero ? TimeSpan.Zero : initialDelay;
            _backoffMultiplier = backoffMultiplier < 1.0 ? 1.0 : backoffMultiplier;
            _useJitter = useJitter;
            _shouldRetry = shouldRetry;
            _logAction = logAction ?? (_ => { });
        }

        /// <inheritdoc />
        public async Task<TResult> InvokeAsync<TQuery, TResult>(
            TQuery query,
            Func<Task<TResult>> next,
            CancellationToken cancellationToken = default)
            where TQuery : IQuery<TResult>
        {
            int attempt = 0;
            var currentDelay = _initialDelay;

            while (true)
            {
                attempt++;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return await next();
                }
                catch (Exception ex) when (attempt <= _maxRetries && (_shouldRetry == null || _shouldRetry(ex)))
                {
                    _logAction($"[Query] {query.GetType().Name} attempt {attempt} failed: {ex.Message}. Retrying...");

                    if (currentDelay > TimeSpan.Zero)
                    {
                        var delayMs = currentDelay.TotalMilliseconds;
                        if (_useJitter)
                        {
                            delayMs += _random.Next(0, (int)Math.Max(1, delayMs * 0.5));
                        }
                        await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
                        currentDelay = TimeSpan.FromMilliseconds(currentDelay.TotalMilliseconds * _backoffMultiplier);
                    }
                }
            }
        }
    }
}
