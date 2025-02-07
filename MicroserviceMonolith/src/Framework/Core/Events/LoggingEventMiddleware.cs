using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Events
{
    public class LoggingEventMiddleware : IEventMiddleware
    {
        private readonly ILogger _logger;
        public LoggingEventMiddleware(ILogger logger) => _logger = logger;

        public async Task InvokeAsync<TEvent>(TEvent @event, Func<Task> next) where TEvent : class
        {
            _logger.Log($"[Event] Start {@event.GetType().Name}");
            await next();
            _logger.Log($"[Event] End {@event.GetType().Name}");
        }
    }
}
