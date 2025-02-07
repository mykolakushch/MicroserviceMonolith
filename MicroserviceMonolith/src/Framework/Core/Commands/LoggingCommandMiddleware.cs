using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// An implementation of <see cref="ICommandMiddleware"/> that logs the start and end of command execution.
    /// </summary>
    public class LoggingCommandMiddleware : ICommandMiddleware
    {
        // Logger instance used to log the details of command execution (start and end).
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="LoggingCommandMiddleware"/> class.
        /// </summary>
        /// <param name="logger">The logger instance used for logging command execution details.</param>
        public LoggingCommandMiddleware(ILogger logger) => _logger = logger;

        /// <summary>
        /// Asynchronously processes the command and logs the start and end of its execution.
        /// </summary>
        /// <typeparam name="TCommand">The type of the command being processed. Must implement <see cref="ICommand"/>.</typeparam>
        /// <param name="command">The command instance that is being processed.</param>
        /// <param name="next">A delegate that represents the next step in the command pipeline.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InvokeAsync<TCommand>(TCommand command, Func<Task> next) where TCommand : ICommand
        {
            // Log the start of the command execution, including the command type.
            _logger.Log($"[Command] Start {command.GetType().Name}");
            
            // Call the next action in the pipeline (next middleware or command handler).
            await next();
            
            // Log the end of the command execution, including the command type.
            _logger.Log($"[Command] End {command.GetType().Name}");
        }
    }
}
