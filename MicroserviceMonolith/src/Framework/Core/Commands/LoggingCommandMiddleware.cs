using System;
using System.Threading;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// Command middleware that logs command execution start, completion, and failure.
    /// </summary>
    public class LoggingCommandMiddleware : ICommandMiddleware
    {
        private readonly Action<string> _logAction;

        /// <summary>
        /// Initializes a new instance of <see cref="LoggingCommandMiddleware"/> with an <see cref="ILogger"/>.
        /// </summary>
        public LoggingCommandMiddleware(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logAction = logger.Log;
        }

        /// <summary>
        /// Initializes a new instance of <see cref="LoggingCommandMiddleware"/> with a standard Microsoft logger.
        /// </summary>
        public LoggingCommandMiddleware(Microsoft.Extensions.Logging.ILogger<LoggingCommandMiddleware> logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logAction = msg => Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "{Message}", msg);
        }

        /// <summary>
        /// Initializes a new instance with a custom log delegate.
        /// </summary>
        public LoggingCommandMiddleware(Action<string> logAction)
        {
            _logAction = logAction ?? throw new ArgumentNullException(nameof(logAction));
        }

        /// <inheritdoc />
        public async Task<TResult> InvokeAsync<TCommand, TResult>(
            TCommand command,
            Func<Task<TResult>> next,
            CancellationToken cancellationToken = default)
            where TCommand : ICommand<TResult>
        {
            var commandName = command.GetType().Name;
            _logAction($"[Command] Start {commandName}");
            try
            {
                var result = await next();
                _logAction($"[Command] End {commandName}");
                return result;
            }
            catch (Exception ex)
            {
                _logAction($"[Command] Failed {commandName}: {ex.Message}");
                throw;
            }
        }
    }
}
