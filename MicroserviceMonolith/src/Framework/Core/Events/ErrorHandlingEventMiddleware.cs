using System;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Events
{
    public class ErrorHandlingEventMiddleware : IEventMiddleware
    {
        private readonly ILogger _logger;
        public ErrorHandlingEventMiddleware(ILogger logger) => _logger = logger;

        public async Task InvokeAsync<TEvent>(TEvent @event, Func<Task> next) where TEvent : class
        {
            try
            {
                await next();
            }
            catch (Exception ex)
            {
                _logger.Log($"[Event] Error in {@event.GetType().Name}: {ex.Message}");
                throw;
            }
        }
    }
}
