using System;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// A middleware that handles error logging for event processing.
    /// If an exception occurs during event handling, it logs the error and rethrows the exception.
    /// </summary>
    public class ErrorHandlingEventMiddleware : IEventMiddleware
    {
        // Logger instance used for logging error details during event handling.
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorHandlingEventMiddleware"/> class.
        /// </summary>
        /// <param name="logger">The logger instance used for logging error details during event processing.</param>
        public ErrorHandlingEventMiddleware(ILogger logger) => _logger = logger;

        /// <summary>
        /// Asynchronously processes the event and logs any error that occurs during its handling.
        /// </summary>
        /// <typeparam name="TEvent">The type of the event being processed. It must be a class type.</typeparam>
        /// <param name="event">The event instance that is being processed.</param>
        /// <param name="next">A delegate representing the next step in the event pipeline.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="Exception">Throws the exception after logging it if an error occurs during event processing.</exception>
        public async Task InvokeAsync<TEvent>(TEvent @event, Func<Task> next) where TEvent : class
        {
            try
            {
                // Proceed to the next action in the event pipeline (e.g., another middleware or event handler).
                await next();
            }
            catch (Exception ex)
            {
                // Log the error with the event type and the exception message if an error occurs during event processing.
                _logger.Log($"[Event] Error in {@event.GetType().Name}: {ex.Message}");

                // Rethrow the exception to ensure proper error handling and prevent the pipeline from continuing.
                throw;
            }
        }
    }
}
