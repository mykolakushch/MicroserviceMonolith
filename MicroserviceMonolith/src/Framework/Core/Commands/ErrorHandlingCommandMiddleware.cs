using System;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Commands
{
    public class ErrorHandlingCommandMiddleware : ICommandMiddleware
    {
        private readonly ILogger _logger;
        public ErrorHandlingCommandMiddleware(ILogger logger) => _logger = logger;

        public async Task InvokeAsync<TCommand>(TCommand command, Func<Task> next) where TCommand : ICommand
        {
            try
            {
                await next();
            }
            catch (Exception ex)
            {
                _logger.Log($"[Command] Error in {command.GetType().Name}: {ex.Message}");
                throw;
            }
        }
    }
}
