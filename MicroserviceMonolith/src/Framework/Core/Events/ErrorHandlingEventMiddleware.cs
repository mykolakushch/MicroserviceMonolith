using System;
using System.Threading;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// Event middleware that logs errors occurring during event publishing.
    /// </summary>
    public class ErrorHandlingEventMiddleware : IEventMiddleware
    {
        private readonly Action<string, Exception> _logErrorAction;

        public ErrorHandlingEventMiddleware(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logErrorAction = (msg, ex) => logger.Log($"[Event] {msg}: {ex}");
        }

        public ErrorHandlingEventMiddleware(Microsoft.Extensions.Logging.ILogger<ErrorHandlingEventMiddleware> logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logErrorAction = (msg, ex) => Microsoft.Extensions.Logging.LoggerExtensions.LogError(logger, ex, "{Message}", msg);
        }

        public ErrorHandlingEventMiddleware(Action<string, Exception> logErrorAction)
        {
            _logErrorAction = logErrorAction ?? throw new ArgumentNullException(nameof(logErrorAction));
        }

        /// <inheritdoc />
        public async Task InvokeAsync<TEvent>(
            TEvent @event,
            Func<Task> next,
            CancellationToken cancellationToken = default)
            where TEvent : class
        {
            try
            {
                await next();
            }
            catch (Exception ex)
            {
                _logErrorAction($"Error in {@event.GetType().Name}", ex);
                throw;
            }
        }
    }
}
