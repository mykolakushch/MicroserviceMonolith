using System;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Events
{
    public class RetryEventMiddleware : IEventMiddleware
    {
        private readonly int _maxRetries;
        private readonly ILogger _logger;
        public RetryEventMiddleware(int maxRetries, ILogger logger)
        {
            _maxRetries = maxRetries;
            _logger = logger;
        }

        public async Task InvokeAsync<TEvent>(TEvent @event, Func<Task> next) where TEvent : class
        {
            int attempts = 0;
            while (true)
            {
                try
                {
                    await next();
                    break;
                }
                catch (Exception ex)
                {
                    attempts++;
                    _logger.Log($"[Event] {@event.GetType().Name} attempt {attempts} failed: {ex.Message}");
                    if (attempts > _maxRetries)
                        throw;
                }
            }
        }
    }
}
