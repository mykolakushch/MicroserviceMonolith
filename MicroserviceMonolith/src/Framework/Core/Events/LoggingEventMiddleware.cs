using System;
using System.Threading;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Context;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// Event middleware that logs the publication lifecycle of events with correlation tracking.
    /// </summary>
    public class LoggingEventMiddleware : IEventMiddleware
    {
        private readonly Action<string> _logAction;
        private readonly ICorrelationContextAccessor? _correlationAccessor;

        public LoggingEventMiddleware(ILogger logger, ICorrelationContextAccessor? correlationAccessor = null)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logAction = logger.Log;
            _correlationAccessor = correlationAccessor;
        }

        public LoggingEventMiddleware(
            Microsoft.Extensions.Logging.ILogger<LoggingEventMiddleware> logger,
            ICorrelationContextAccessor? correlationAccessor = null)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logAction = msg => Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "{Message}", msg);
            _correlationAccessor = correlationAccessor;
        }

        public LoggingEventMiddleware(Action<string> logAction, ICorrelationContextAccessor? correlationAccessor = null)
        {
            _logAction = logAction ?? throw new ArgumentNullException(nameof(logAction));
            _correlationAccessor = correlationAccessor;
        }

        /// <inheritdoc />
        public async Task InvokeAsync<TEvent>(
            TEvent @event,
            Func<Task> next,
            CancellationToken cancellationToken = default)
            where TEvent : class
        {
            var eventName = @event.GetType().Name;
            var cid = _correlationAccessor?.CorrelationContext?.CorrelationId;
            var prefix = !string.IsNullOrWhiteSpace(cid) ? $"[CID: {cid}] " : string.Empty;

            _logAction($"{prefix}[Event] Start {eventName}");
            try
            {
                await next();
                _logAction($"{prefix}[Event] End {eventName}");
            }
            catch (Exception ex)
            {
                _logAction($"{prefix}[Event] Failed {eventName}: {ex}");
                throw;
            }
        }
    }
}
