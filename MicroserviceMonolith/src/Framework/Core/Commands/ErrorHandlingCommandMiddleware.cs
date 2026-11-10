using System;
using System.Threading;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// Middleware for capturing and logging unhandled errors during command processing.
    /// </summary>
    public class ErrorHandlingCommandMiddleware : ICommandMiddleware
    {
        private readonly Action<string, Exception> _logErrorAction;

        public ErrorHandlingCommandMiddleware(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logErrorAction = (msg, ex) => logger.Log($"[Command] {msg}: {ex}");
        }

        public ErrorHandlingCommandMiddleware(Microsoft.Extensions.Logging.ILogger<ErrorHandlingCommandMiddleware> logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _logErrorAction = (msg, ex) => Microsoft.Extensions.Logging.LoggerExtensions.LogError(logger, ex, "{Message}", msg);
        }

        public ErrorHandlingCommandMiddleware(Action<string, Exception> logErrorAction)
        {
            _logErrorAction = logErrorAction ?? throw new ArgumentNullException(nameof(logErrorAction));
        }

        /// <inheritdoc />
        public async Task<TResult> InvokeAsync<TCommand, TResult>(
            TCommand command,
            Func<Task<TResult>> next,
            CancellationToken cancellationToken = default)
            where TCommand : ICommand<TResult>
        {
            try
            {
                return await next();
            }
            catch (Exception ex)
            {
                _logErrorAction($"Error in {command.GetType().Name}", ex);
                throw;
            }
        }
    }
}
