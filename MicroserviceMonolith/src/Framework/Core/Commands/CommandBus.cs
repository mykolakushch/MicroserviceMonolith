using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Common;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// Thread-safe in-memory command bus facilitating centralized command dispatch.
    /// Supports manual handler/middleware registration as well as resolution from <see cref="IServiceProvider"/>.
    /// </summary>
    public class CommandBus : ICommandBus
    {
        private readonly IServiceProvider? _serviceProvider;
        private readonly ConcurrentDictionary<Type, Func<object, CancellationToken, Task<object?>>> _handlers = new();
        private readonly List<ICommandMiddleware> _middlewares = new();
        private readonly object _middlewareLock = new();

        /// <summary>
        /// Initializes a new instance of <see cref="CommandBus"/> for manual registration.
        /// </summary>
        public CommandBus() : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="CommandBus"/> with dependency injection support.
        /// </summary>
        /// <param name="serviceProvider">Optional service provider to resolve handlers and middlewares.</param>
        public CommandBus(IServiceProvider? serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Registers a command handler for a command with no return value.
        /// </summary>
        public void RegisterHandler<TCommand>(ICommandHandler<TCommand> handler) where TCommand : ICommand
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _handlers[typeof(TCommand)] = async (cmd, ct) =>
            {
                await handler.HandleAsync((TCommand)cmd, ct);
                return Unit.Value;
            };
        }

        /// <summary>
        /// Registers a command handler for a command that returns a result.
        /// </summary>
        public void RegisterHandler<TCommand, TResult>(ICommandHandler<TCommand, TResult> handler) where TCommand : ICommand<TResult>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _handlers[typeof(TCommand)] = async (cmd, ct) =>
            {
                var result = await handler.HandleAsync((TCommand)cmd, ct);
                return (object?)result;
            };
        }

        /// <summary>
        /// Adds a middleware component to the command pipeline.
        /// </summary>
        public void AddMiddleware(ICommandMiddleware middleware)
        {
            if (middleware == null) throw new ArgumentNullException(nameof(middleware));
            lock (_middlewareLock)
            {
                _middlewares.Add(middleware);
            }
        }

        /// <inheritdoc />
        public async Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            await SendAsync<TCommand, Unit>(command, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            var commandType = command.GetType();
            var method = typeof(CommandBus)
                .GetMethods()
                .First(m => m.Name == nameof(SendAsync) && m.IsGenericMethod && m.GetGenericArguments().Length == 2)
                .MakeGenericMethod(commandType, typeof(TResult));

            var task = (Task<TResult>)method.Invoke(this, new object[] { command, cancellationToken })!;
            return await task;
        }

        /// <inheritdoc />
        public async Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand<TResult>
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            cancellationToken.ThrowIfCancellationRequested();

            // Collect all middlewares (both manually added and from DI)
            var allMiddlewares = new List<ICommandMiddleware>();
            lock (_middlewareLock)
            {
                allMiddlewares.AddRange(_middlewares);
            }

            IServiceScope? scope = null;
            try
            {
                IServiceProvider? sp = _serviceProvider;
                if (sp != null)
                {
                    scope = sp.CreateScope();
                    sp = scope.ServiceProvider;
                    allMiddlewares.AddRange(sp.GetServices<ICommandMiddleware>());
                }

                Func<CancellationToken, Task<TResult>> handlerFunc;

                if (_handlers.TryGetValue(typeof(TCommand), out var manualHandler))
                {
                    handlerFunc = async ct =>
                    {
                        var res = await manualHandler(command, ct);
                        return (TResult)res!;
                    };
                }
                else if (sp != null)
                {
                    var handlerTypeWithResult = typeof(ICommandHandler<TCommand, TResult>);
                    var handlerWithResult = sp.GetService(handlerTypeWithResult) as ICommandHandler<TCommand, TResult>;
                    if (handlerWithResult != null)
                    {
                        handlerFunc = ct => handlerWithResult.HandleAsync(command, ct);
                    }
                    else if (typeof(TResult) == typeof(Unit) && typeof(ICommand).IsAssignableFrom(typeof(TCommand)))
                    {
                        var handlerTypeVoid = typeof(ICommandHandler<>).MakeGenericType(typeof(TCommand));
                        var handlerVoid = sp.GetService(handlerTypeVoid);
                        if (handlerVoid != null)
                        {
                            var handleMethod = handlerTypeVoid.GetMethod("HandleAsync")!;
                            handlerFunc = async ct =>
                            {
                                var task = (Task)handleMethod.Invoke(handlerVoid, new object[] { command, ct })!;
                                await task;
                                return (TResult)(object)Unit.Value;
                            };
                        }
                        else
                        {
                            throw new InvalidOperationException($"No command handler registered for command '{typeof(TCommand).FullName}'.");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"No command handler registered for command '{typeof(TCommand).FullName}'.");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"No command handler registered for command '{typeof(TCommand).FullName}'.");
                }

                // Build the execution pipeline
                Func<Task<TResult>> pipeline = () => handlerFunc(cancellationToken);

                for (int i = allMiddlewares.Count - 1; i >= 0; i--)
                {
                    var middleware = allMiddlewares[i];
                    var next = pipeline;
                    pipeline = () => middleware.InvokeAsync(command, next, cancellationToken);
                }

                return await pipeline();
            }
            finally
            {
                scope?.Dispose();
            }
        }
    }
}
