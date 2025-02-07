using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// Implements a command bus that facilitates centralized command processing.
    /// Allows registering command handlers and adding middleware for command interception and processing.
    /// </summary>
    public class CommandBus : ICommandBus
    {
        /// <summary>
        /// A thread-safe dictionary storing registered command handlers.
        /// Each entry maps a command type to its corresponding asynchronous handler function.
        /// </summary>
        private readonly ConcurrentDictionary<Type, Func<object, Task>> _handlers = new();

        /// <summary>
        /// A list of registered middleware components that can intercept and modify command execution.
        /// Middleware is executed in reverse order of registration.
        /// </summary>
        private readonly List<ICommandMiddleware> _middlewares = new();

        /// <summary>
        /// Registers a command handler for a specific command type.
        /// Each command type can have only one handler.
        /// </summary>
        /// <typeparam name="TCommand">The type of command being registered.</typeparam>
        /// <param name="handler">An instance of a command handler that processes the given command type.</param>
        public void RegisterHandler<TCommand>(ICommandHandler<TCommand> handler) where TCommand : ICommand
        {
            _handlers[typeof(TCommand)] = (cmd) => handler.HandleAsync((TCommand)cmd);
        }

        /// <summary>
        /// Adds a middleware component to the command bus.
        /// Middleware can modify or extend command processing behavior.
        /// </summary>
        /// <param name="middleware">An instance of middleware to be added to the pipeline.</param>
        public void AddMiddleware(ICommandMiddleware middleware)
        {
            _middlewares.Add(middleware);
        }

        /// <summary>
        /// Sends a command to the command bus for processing.
        /// If a handler is registered for the command type, it will be executed, along with any middleware.
        /// </summary>
        /// <typeparam name="TCommand">The type of command being sent.</typeparam>
        /// <param name="command">The command instance to be processed.</param>
        /// <exception cref="InvalidOperationException">Thrown if no handler is found for the command type.</exception>
        public async Task SendAsync<TCommand>(TCommand command) where TCommand : ICommand
        {
            if (_handlers.TryGetValue(typeof(TCommand), out var handler))
            {
                // Initial execution pipeline, starting with the actual command handler
                Func<Task> pipeline = () => handler(command);

                // Wrap the pipeline with middleware, executing them in reverse order
                foreach (var middleware in _middlewares.AsEnumerable().Reverse())
                {
                    var next = pipeline;
                    pipeline = () => middleware.InvokeAsync(command, next);
                }

                // Execute the final pipeline
                await pipeline();
            }
            else
            {
                throw new InvalidOperationException("Handler not found");
            }
        }
    }
}
