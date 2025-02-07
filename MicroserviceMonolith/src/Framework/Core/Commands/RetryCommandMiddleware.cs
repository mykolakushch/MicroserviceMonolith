using System;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// A middleware that retries the execution of a command if an exception occurs.
    /// </summary>
    public class RetryCommandMiddleware : ICommandMiddleware
    {
        // The maximum number of retry attempts allowed for a failed command execution.
        private readonly int _maxRetries;

        // Logger instance used for logging retry attempts and failures.
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RetryCommandMiddleware"/> class.
        /// </summary>
        /// <param name="maxRetries">The maximum number of retries allowed for the command execution.</param>
        /// <param name="logger">The logger instance used for logging retry attempts and failure messages.</param>
        public RetryCommandMiddleware(int maxRetries, ILogger logger)
        {
            _maxRetries = maxRetries;
            _logger = logger;
        }

        /// <summary>
        /// Asynchronously attempts to process a command with retry logic.
        /// If an exception occurs during command execution, it will retry until the maximum number of retries is reached.
        /// </summary>
        /// <typeparam name="TCommand">The type of the command being processed. Must implement <see cref="ICommand"/>.</typeparam>
        /// <param name="command">The command instance that is being processed.</param>
        /// <param name="next">A delegate representing the next step in the command pipeline.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="Exception">Throws the exception if the maximum retry attempts are exceeded.</exception>
        public async Task InvokeAsync<TCommand>(TCommand command, Func<Task> next) where TCommand : ICommand
        {
            int attempts = 0;

            // Retry loop to attempt command execution multiple times if an exception occurs.
            while (true)
            {
                try
                {
                    // Execute the next action in the command pipeline (e.g., the next middleware or handler).
                    await next();
                    break; // Exit the loop if command execution succeeds.
                }
                catch (Exception ex)
                {
                    // Increment the number of attempts after each failure.
                    attempts++;

                    // Log the failure attempt and the exception message.
                    _logger.Log($"[Command] {command.GetType().Name} attempt {attempts} failed: {ex.Message}");

                    // If the maximum number of retries has been reached, rethrow the exception.
                    if (attempts >= _maxRetries)
                        throw;
                }
            }
        }
    }
}
