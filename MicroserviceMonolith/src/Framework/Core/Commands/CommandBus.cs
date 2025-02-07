using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Commands
{
    public class CommandBus : ICommandBus
    {
        private readonly ConcurrentDictionary<Type, Func<object, Task>> _handlers = new();
        private readonly List<ICommandMiddleware> _middlewares = new();

        public void RegisterHandler<TCommand>(ICommandHandler<TCommand> handler) where TCommand : ICommand
        {
            _handlers[typeof(TCommand)] = (cmd) => handler.HandleAsync((TCommand)cmd);
        }

        public void AddMiddleware(ICommandMiddleware middleware)
        {
            _middlewares.Add(middleware);
        }

        public async Task SendAsync<TCommand>(TCommand command) where TCommand : ICommand
        {
            if (_handlers.TryGetValue(typeof(TCommand), out var handler))
            {
                Func<Task> pipeline = () => handler(command);
                foreach (var middleware in _middlewares.AsEnumerable().Reverse())
                {
                    var next = pipeline;
                    pipeline = () => middleware.InvokeAsync(command, next);
                }
                await pipeline();
            }
            else
            {
                throw new InvalidOperationException("Handler not found");
            }
        }
    }
}
