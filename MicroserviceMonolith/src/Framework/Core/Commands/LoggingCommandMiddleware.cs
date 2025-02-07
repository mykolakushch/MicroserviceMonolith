using System.Threading.Tasks;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Commands
{
    public class LoggingCommandMiddleware : ICommandMiddleware
    {
        private readonly ILogger _logger;
        public LoggingCommandMiddleware(ILogger logger) => _logger = logger;

        public async Task InvokeAsync<TCommand>(TCommand command, Func<Task> next) where TCommand : ICommand
        {
            _logger.Log($"[Command] Start {command.GetType().Name}");
            await next();
            _logger.Log($"[Command] End {command.GetType().Name}");
        }
    }
}
