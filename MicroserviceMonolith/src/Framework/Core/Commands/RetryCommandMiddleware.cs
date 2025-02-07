using System;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Commands
{
    public class RetryCommandMiddleware : ICommandMiddleware
    {
        private readonly int _maxRetries;
        private readonly ILogger _logger;
        public RetryCommandMiddleware(int maxRetries, ILogger logger)
        {
            _maxRetries = maxRetries;
            _logger = logger;
        }

        public async Task InvokeAsync<TCommand>(TCommand command, Func<Task> next) where TCommand : ICommand
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
                    _logger.Log($"[Command] {command.GetType().Name} attempt {attempts} failed: {ex.Message}");
                    if (attempts > _maxRetries)
                        throw;
                }
            }
        }
    }
}
