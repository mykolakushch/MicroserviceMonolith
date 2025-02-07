using System;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// Middleware for handling errors during command execution.
    /// Logs exceptions and rethrows them to ensure error propagation.
    /// </summary>
    public class ErrorHandlingCommandMiddleware : ICommandMiddleware
    {
        /// <summary>
        /// Logger instance used for logging command execution errors.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorHandlingCommandMiddleware"/> class.
        /// </summary>
        /// <param name="logger">An instance of <see cref="ILogger"/> for logging errors.</param>
        public ErrorHandlingCommandMiddleware(ILogger logger) => _logger = logger;

        /// <summary>
        /// Intercepts command execution to handle errors.
        /// If an exception occurs, it logs the error and rethrows the exception.
        /// </summary>
        /// <typeparam name="TCommand">The type of command being processed.</typeparam>
        /// <param name="command">The command instance being executed.</param>
        /// <param name="next">The next delegate in the middleware pipeline.</param>
        /// <returns>A task representing the asynchronous execution of the command.</returns>
        public async Task InvokeAsync<TCommand>(TCommand command, Func<Task> next) where TCommand : ICommand
        {
            try
            {
                await next(); // Execute the next middleware or command handler
            }
            catch (Exception ex)
            {
                _logger.Log($"[Command] Error in {command.GetType().Name}: {ex.Message}");
                throw; // Rethrow the exception to ensure error propagation
            }
        }
    }
}
