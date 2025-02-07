using System;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// An implementation of <see cref="IEventMiddleware"/> that handles retrying event processing.
    /// This middleware retries processing the event up to a specified number of attempts if an exception occurs.
    /// </summary>
    public class RetryEventMiddleware : IEventMiddleware
    {
        private readonly int _maxRetries;  // The maximum number of retry attempts allowed.
        private readonly ILogger _logger;  // Logger instance used for logging retry attempts and errors.

        /// <summary>
        /// Initializes a new instance of the <see cref="RetryEventMiddleware"/> class.
        /// </summary>
        /// <param name="maxRetries">The maximum number of retries allowed before giving up.</param>
        /// <param name="logger">The logger used to log retry attempts and failures.</param>
        public RetryEventMiddleware(int maxRetries, ILogger logger)
        {
            _maxRetries = maxRetries;
            _logger = logger;
        }

        /// <summary>
        /// Asynchronously processes the event with retry logic. If an exception occurs during event processing,
        /// the middleware will attempt to process the event again, up to the maximum number of retries.
        /// </summary>
        /// <typeparam name="TEvent">The type of the event being processed. Must be a class.</typeparam>
        /// <param name="event">The event instance to be processed.</param>
        /// <param name="next">A function representing the next action in the event processing pipeline (the next middleware or event handler).</param>
        /// <returns>A task representing the asynchronous operation of processing the event.</returns>
        public async Task InvokeAsync<TEvent>(TEvent @event, Func<Task> next) where TEvent : class
        {
            int attempts = 0;  // Tracks the number of retry attempts.
            while (true)
            {
                try
                {
                    // Attempt to process the event by invoking the next step in the event pipeline.
                    await next();
                    break;  // If successful, exit the loop.
                }
                catch (Exception ex)
                {
                    attempts++;  // Increment the attempt counter.
                    // Log the failure and include the exception message.
                    _logger.Log($"[Event] {@event.GetType().Name} attempt {attempts} failed: {ex.Message}");
                    
                    // If the maximum retry attempts are reached, throw the exception and stop retrying.
                    if (attempts > _maxRetries)
                        throw;
                }
            }
        }
    }
}
