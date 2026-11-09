using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Common;
using MicroserviceMonolith.Core.Context;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// Thread-safe in-memory command bus facilitating centralized command dispatch.
    /// Supports manual handler/middleware registration as well as resolution from <see cref="IServiceProvider"/>.
    /// Optimized for high throughput and Native AOT with zero reflection on the execution hot path.
    /// </summary>
    public class CommandBus : ICommandBus
    {
        private readonly IServiceProvider? _serviceProvider;
        private readonly ICorrelationContextAccessor? _correlationAccessor;
        private readonly ConcurrentDictionary<Type, Func<object, CancellationToken, Task<object?>>> _handlers = new();
        private readonly List<ICommandMiddleware> _middlewares = new();
        private readonly object _middlewareLock = new();

        /// <summary>
        /// Initializes a new instance of <see cref="CommandBus"/> for manual registration.
        /// </summary>
        public CommandBus() : this(null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="CommandBus"/> with dependency injection support.
        /// </summary>
        /// <param name="serviceProvider">Optional service provider to resolve handlers and middlewares.</param>
        public CommandBus(IServiceProvider? serviceProvider) : this(serviceProvider, serviceProvider?.GetService<ICorrelationContextAccessor>())
        {
        }

        /// <summary>
        /// Initializes a new instance with service provider and correlation context accessor.
        /// </summary>
        public CommandBus(IServiceProvider? serviceProvider, ICorrelationContextAccessor? correlationAccessor)
        {
            _serviceProvider = serviceProvider;
            _correlationAccessor = correlationAccessor;
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
        public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            return CommandSenderCache<TResult>.SendAsync(this, command, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand<TResult>
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            cancellationToken.ThrowIfCancellationRequested();

            // Auto-initialize correlation context if not present
            if (_correlationAccessor != null && _correlationAccessor.CorrelationContext == null)
            {
                _correlationAccessor.CorrelationContext = new CorrelationContext();
            }

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
                    // Direct generic resolution without runtime reflection!
                    var handler = sp.GetService<ICommandHandler<TCommand, TResult>>();
                    if (handler != null)
                    {
                        handlerFunc = ct => handler.HandleAsync(command, ct);
                    }
                    else if (typeof(TResult) == typeof(Unit) && typeof(ICommand).IsAssignableFrom(typeof(TCommand)))
                    {
                        // Fallback when ICommandHandler<TCommand> was registered manually without Unit adapter
                        var voidHandlerType = typeof(ICommandHandler<>).MakeGenericType(typeof(TCommand));
                        var voidHandler = sp.GetService(voidHandlerType);
                        if (voidHandler != null)
                        {
                            var method = voidHandlerType.GetMethod(nameof(ICommandHandler<ICommand>.HandleAsync))!;
                            handlerFunc = async ct =>
                            {
                                var task = (Task)method.Invoke(voidHandler, new object[] { command, ct })!;
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

        /// <summary>
        /// Cached dispatch table for commands to avoid repeated reflection lookups on SendAsync&lt;TResult&gt;.
        /// </summary>
        private static class CommandSenderCache<TResult>
        {
            private static readonly ConcurrentDictionary<Type, Func<CommandBus, object, CancellationToken, Task<TResult>>> _invokers = new();

            public static Task<TResult> SendAsync(CommandBus bus, ICommand<TResult> command, CancellationToken ct)
            {
                var invoker = _invokers.GetOrAdd(command.GetType(), cmdType =>
                {
                    var method = typeof(CommandBus)
                        .GetMethods()
                        .First(m => m.Name == nameof(CommandBus.SendAsync) && m.IsGenericMethod && m.GetGenericArguments().Length == 2)
                        .MakeGenericMethod(cmdType, typeof(TResult));

                    return (b, cmd, token) => (Task<TResult>)method.Invoke(b, new object[] { cmd, token })!;
                });

                return invoker(bus, command, ct);
            }
        }
    }
}
