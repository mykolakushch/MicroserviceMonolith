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
    /// Dispatch targets are cached per command type, so reflection runs only once per command type (when its cache
    /// entry is created) and not on every call. The framework as a whole is not trimming/Native AOT safe:
    /// that one-time cache creation and the assembly scanning rely on runtime reflection.
    /// </summary>
    public class CommandBus : ICommandBus
    {
        private readonly IServiceProvider? _serviceProvider;
        private readonly ICorrelationContextAccessor? _correlationAccessor;
        private readonly bool _shareScope;
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
            : this(serviceProvider, correlationAccessor, false)
        {
        }

        /// <summary>
        /// Initializes a new instance with service provider, correlation context accessor and the scope sharing mode.
        /// </summary>
        /// <param name="shareScopeBetweenNestedDispatches">
        /// When true, commands, queries and events dispatched from inside a handler reuse the DI scope of the
        /// outermost dispatch instead of creating their own (the outermost dispatch owns and disposes it).
        /// </param>
        public CommandBus(IServiceProvider? serviceProvider, ICorrelationContextAccessor? correlationAccessor, bool shareScopeBetweenNestedDispatches)
        {
            _serviceProvider = serviceProvider;
            _correlationAccessor = correlationAccessor;
            _shareScope = shareScopeBetweenNestedDispatches;
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
            AmbientScopeEntry? previousAmbient = null;
            try
            {
                IServiceProvider? sp = _serviceProvider;
                if (sp != null)
                {
                    if (_shareScope && AmbientDispatchScope.TryGet(_serviceProvider!, out var sharedScope))
                    {
                        // Nested dispatch: reuse the scope of the outermost one
                        sp = sharedScope;
                    }
                    else
                    {
                        scope = sp.CreateScope();
                        sp = scope.ServiceProvider;
                        if (_shareScope)
                        {
                            previousAmbient = AmbientDispatchScope.Set(_serviceProvider!, sp);
                        }
                    }
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
                    // Direct generic resolution
                    var handler = sp.GetService<ICommandHandler<TCommand, TResult>>();
                    if (handler != null)
                    {
                        handlerFunc = ct => handler.HandleAsync(command, ct);
                    }
                    else if (typeof(TResult) == typeof(Unit) && typeof(ICommand).IsAssignableFrom(typeof(TCommand)))
                    {
                        // Fallback when ICommandHandler<TCommand> was registered manually without Unit adapter.
                        // Since ICommandHandler<TCommand> inherits ICommandHandler<TCommand, Unit>, cast directly.
                        // The closed service type is computed once per command type, not on every call.
                        var voidHandler = sp.GetService(VoidHandlerServiceType<TCommand>.Value);
                        if (voidHandler is ICommandHandler<TCommand, TResult> unitHandler)
                        {
                            handlerFunc = ct => unitHandler.HandleAsync(command, ct);
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
                if (scope != null)
                {
                    if (_shareScope)
                    {
                        AmbientDispatchScope.Restore(previousAmbient);
                    }
                    scope.Dispose();
                }
            }
        }

        /// <summary>
        /// Closed service type <c>ICommandHandler&lt;TCommand&gt;</c>, computed once per command type.
        /// Only touched for commands that implement <see cref="ICommand"/>.
        /// </summary>
        private static class VoidHandlerServiceType<TCommand>
        {
            public static readonly Type Value = typeof(ICommandHandler<>).MakeGenericType(typeof(TCommand));
        }

        /// <summary>
        /// Calls the typed <c>SendAsync&lt;TCommand, TResult&gt;</c> for a command known only through <see cref="ICommand{TResult}"/>.
        /// </summary>
        private abstract class CommandInvokerBase<TResult>
        {
            public abstract Task<TResult> InvokeAsync(CommandBus bus, ICommand<TResult> command, CancellationToken ct);
        }

        private sealed class CommandInvoker<TCommand, TResult> : CommandInvokerBase<TResult> where TCommand : ICommand<TResult>
        {
            public override Task<TResult> InvokeAsync(CommandBus bus, ICommand<TResult> command, CancellationToken ct)
                => bus.SendAsync<TCommand, TResult>((TCommand)command, ct);
        }

        /// <summary>
        /// Per-result-type cache of invokers keyed by the runtime command type. Reflection is used once per command
        /// type to create the invoker; every later call is a plain virtual call (no MethodInfo.Invoke, no argument arrays).
        /// </summary>
        private static class CommandSenderCache<TResult>
        {
            private static readonly ConcurrentDictionary<Type, CommandInvokerBase<TResult>> _invokers = new();

            public static Task<TResult> SendAsync(CommandBus bus, ICommand<TResult> command, CancellationToken ct)
            {
                var invoker = _invokers.GetOrAdd(command.GetType(), cmdType =>
                    (CommandInvokerBase<TResult>)Activator.CreateInstance(
                        typeof(CommandInvoker<,>).MakeGenericType(cmdType, typeof(TResult)))!);

                return invoker.InvokeAsync(bus, command, ct);
            }
        }
    }
}
