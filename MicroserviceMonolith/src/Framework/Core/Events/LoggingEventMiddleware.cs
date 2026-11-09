using System;
using System.Threading;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// Event middleware that logs the publication lifecycle of events.
    /// </summary>
    public class LoggingEventMiddleware : IEventMiddleware
    {
        private readonly Action<string> _logAction;

        public LoggingEventMiddleware(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logAction = logger.Log;
        }

        public LoggingEventMiddleware(Microsoft.Extensions.Logging.ILogger<LoggingEventMiddleware> logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logAction = msg => Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "{Message}", msg);
        }

        public LoggingEventMiddleware(Action<string> logAction)
        {
            _logAction = logAction ?? throw new ArgumentNullException(nameof(logAction));
        }

        /// <inheritdoc />
        public async Task InvokeAsync<TEvent>(
            TEvent @event,
            Func<Task> next,
            CancellationToken cancellationToken = default)
            where TEvent : class
        {
            var eventName = @event.GetType().Name;
            _logAction($"[Event] Start {eventName}");
            try
            {
                await next();
                _logAction($"[Event] End {eventName}");
            }
            catch (Exception ex)
            {
                _logAction($"[Event] Failed {eventName}: {ex.Message}");
                throw;
            }
        }
    }
}
