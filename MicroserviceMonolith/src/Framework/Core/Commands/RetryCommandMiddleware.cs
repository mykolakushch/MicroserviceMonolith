using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// Middleware that retries command execution with optional exponential backoff and jitter upon failure.
    /// </summary>
    /// <remarks>
    /// <b>Retrying a command is only safe when running it again has the same effect as running it once.</b>
    /// If the first attempt did its work but then failed (for example a timeout while reporting the result), a retry
    /// repeats the side effect: a payment is charged twice, a mail is sent twice. Therefore, by default, only commands
    /// marked with <see cref="IdempotentCommandAttribute"/> are retried; every other command runs once and its failure
    /// is propagated. Pass <c>retryNonIdempotentCommands: true</c> to retry every command (and make the commands idempotent
    /// yourself, for example with an idempotency key).
    /// </remarks>
    public class RetryCommandMiddleware : ICommandMiddleware
    {
        private readonly int _maxRetries;
        private readonly TimeSpan _initialDelay;
        private readonly double _backoffMultiplier;
        private readonly bool _useJitter;
        private readonly Func<Exception, bool>? _shouldRetry;
        private readonly Action<string> _logAction;
        private readonly bool _retryNonIdempotentCommands;
        private static readonly ConcurrentDictionary<Type, bool> IdempotentTypes = new();

        public RetryCommandMiddleware(int maxRetries, ILogger logger)
            : this(maxRetries, TimeSpan.FromMilliseconds(50), 2.0, true, null, m => logger.LogWarning("{Message}", m))
        {
        }

        public RetryCommandMiddleware(
            int maxRetries,
            TimeSpan initialDelay,
            double backoffMultiplier = 2.0,
            bool useJitter = true,
            Func<Exception, bool>? shouldRetry = null,
            Action<string>? logAction = null,
            bool retryNonIdempotentCommands = false)
        {
            if (maxRetries < 0) throw new ArgumentOutOfRangeException(nameof(maxRetries), "Max retries cannot be negative.");
            _maxRetries = maxRetries;
            _initialDelay = initialDelay < TimeSpan.Zero ? TimeSpan.Zero : initialDelay;
            _backoffMultiplier = backoffMultiplier < 1.0 ? 1.0 : backoffMultiplier;
            _useJitter = useJitter;
            _shouldRetry = shouldRetry;
            _logAction = logAction ?? (_ => { });
            _retryNonIdempotentCommands = retryNonIdempotentCommands;
        }

        /// <inheritdoc />
        private static bool IsIdempotent(Type commandType) =>
            IdempotentTypes.GetOrAdd(commandType, t => Attribute.IsDefined(t, typeof(IdempotentCommandAttribute), inherit: true));

        public async Task<TResult> InvokeAsync<TCommand, TResult>(
            TCommand command,
            Func<Task<TResult>> next,
            CancellationToken cancellationToken = default)
            where TCommand : ICommand<TResult>
        {
            int attempt = 0;
            var currentDelay = _initialDelay;
            var maxRetries = _retryNonIdempotentCommands || IsIdempotent(command.GetType()) ? _maxRetries : 0;

            while (true)
            {
                attempt++;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return await next();
                }
                // Never retry once the caller has cancelled; other failures (including timeouts that surface
                // as OperationCanceledException without the caller's token being cancelled) remain retriable.
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && attempt <= maxRetries && (_shouldRetry == null || _shouldRetry(ex)))
                {
                    _logAction($"[Command] {command.GetType().Name} attempt {attempt} failed: {ex.Message}. Retrying...");

                    if (currentDelay > TimeSpan.Zero)
                    {
                        var delayMs = currentDelay.TotalMilliseconds;
                        if (_useJitter)
                        {
                            delayMs += Random.Shared.Next(0, (int)Math.Max(1, delayMs * 0.5));
                        }
                        await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
                        currentDelay = TimeSpan.FromMilliseconds(currentDelay.TotalMilliseconds * _backoffMultiplier);
                    }
                }
            }
        }
    }
}
