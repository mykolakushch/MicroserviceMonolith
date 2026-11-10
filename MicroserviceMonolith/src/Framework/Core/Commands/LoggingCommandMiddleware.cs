using System;
using System.Threading;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Context;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// Command middleware that logs command execution start, completion, and failure with ambient correlation tracking.
    /// </summary>
    public class LoggingCommandMiddleware : ICommandMiddleware
    {
        private readonly Action<string> _logAction;
        private readonly Action<string> _failureAction;
        private readonly ICorrelationContextAccessor? _correlationAccessor;

        public LoggingCommandMiddleware(ILogger logger, ICorrelationContextAccessor? correlationAccessor = null)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logAction = logger.Log;
            _failureAction = m => logger.Log(LogSeverity.Error, m);
            _correlationAccessor = correlationAccessor;
        }

        public LoggingCommandMiddleware(
            Microsoft.Extensions.Logging.ILogger<LoggingCommandMiddleware> logger,
            ICorrelationContextAccessor? correlationAccessor = null)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logAction = msg => Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "{Message}", msg);
            _failureAction = msg => Microsoft.Extensions.Logging.LoggerExtensions.LogError(logger, "{Message}", msg);
            _correlationAccessor = correlationAccessor;
        }

        public LoggingCommandMiddleware(Action<string> logAction, ICorrelationContextAccessor? correlationAccessor = null)
        {
            _logAction = logAction ?? throw new ArgumentNullException(nameof(logAction));
            _failureAction = _logAction;
            _correlationAccessor = correlationAccessor;
        }

        /// <inheritdoc />
        public async Task<TResult> InvokeAsync<TCommand, TResult>(
            TCommand command,
            Func<Task<TResult>> next,
            CancellationToken cancellationToken = default)
            where TCommand : ICommand<TResult>
        {
            var commandName = command.GetType().Name;
            var cid = _correlationAccessor?.CorrelationContext?.CorrelationId;
            var prefix = !string.IsNullOrWhiteSpace(cid) ? $"[CID: {cid}] " : string.Empty;

            _logAction($"{prefix}[Command] Start {commandName}");
            try
            {
                var result = await next();
                _logAction($"{prefix}[Command] End {commandName}");
                return result;
            }
            catch (Exception ex)
            {
                _failureAction($"{prefix}[Command] Failed {commandName}: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }
    }
}
