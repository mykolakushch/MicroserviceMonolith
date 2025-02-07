using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Events
{
    /// <summary>
    /// An implementation of <see cref="IEventMiddleware"/> that logs the start and end of event processing.
    /// This middleware logs the event type when the event processing begins and ends.
    /// </summary>
    public class LoggingEventMiddleware : IEventMiddleware
    {
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="LoggingEventMiddleware"/> class.
        /// </summary>
        /// <param name="logger">The logger used to log event processing details.</param>
        public LoggingEventMiddleware(ILogger logger) => _logger = logger;

        /// <summary>
        /// Asynchronously processes the event and logs the start and end of its execution.
        /// </summary>
        /// <typeparam name="TEvent">The type of the event being processed. Must be a class.</typeparam>
        /// <param name="event">The event instance to be processed.</param>
        /// <param name="next">A function representing the next action in the event processing pipeline (the next middleware or event handler).</param>
        /// <returns>A task representing the asynchronous operation of processing the event.</returns>
        public async Task InvokeAsync<TEvent>(TEvent @event, Func<Task> next) where TEvent : class
        {
            // Log the start of the event processing, including the event type.
            _logger.Log($"[Event] Start {@event.GetType().Name}");
            
            // Proceed to the next step in the event processing pipeline.
            await next();
            
            // Log the end of the event processing, including the event type.
            _logger.Log($"[Event] End {@event.GetType().Name}");
        }
    }
}
