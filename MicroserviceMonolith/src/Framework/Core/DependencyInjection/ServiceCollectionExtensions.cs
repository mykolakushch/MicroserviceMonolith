using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MicroserviceMonolith.Core.Commands;
using MicroserviceMonolith.Core.Context;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Core.Execution;
using MicroserviceMonolith.Core.Logging;
using MicroserviceMonolith.Core.Modules;
using MicroserviceMonolith.Core.Outbox;
using MicroserviceMonolith.Core.Queries;

namespace MicroserviceMonolith.Core.DependencyInjection
{
    /// <summary>
    /// Extension methods for registering MicroserviceMonolith framework components into <see cref="IServiceCollection"/>.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers MicroserviceMonolith core services, bus handlers, execution queue, and modules.
        /// </summary>
        public static IServiceCollection AddMicroserviceMonolith(
            this IServiceCollection services,
            Action<MicroserviceMonolithOptions>? configure = null)
        {
            var options = new MicroserviceMonolithOptions();
            configure?.Invoke(options);

            // Logging
            services.TryAddSingleton<ILogger, ConsoleLogger>();

            // Context Accessor
            services.TryAddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();

            // Execution Queue
            services.TryAddSingleton(sp => new ExecutionQueue(options.ExecutionQueueOptions));

            // Module Manager
            services.TryAddSingleton<ModuleManager>();

            // Command Bus
            services.TryAddSingleton<ICommandBus>(sp => new CommandBus(sp));

            // Query Bus
            services.TryAddSingleton<IQueryBus>(sp => new QueryBus(sp));

            // Event Bus
            services.TryAddSingleton<EventBus>(sp => new EventBus(sp));
            services.TryAddSingleton<IEventPublisher>(sp => sp.GetRequiredService<EventBus>());
            services.TryAddSingleton<IEventSubscriber>(sp => sp.GetRequiredService<EventBus>());

            // Outbox
            if (options.EnableOutbox)
            {
                services.TryAddSingleton<IOutboxStore, InMemoryOutboxStore>();
                services.TryAddSingleton<IOutboxProcessor, OutboxProcessor>();
            }

            // Assembly scanning for handlers and modules
            if (options.AssembliesToScan.Count > 0)
            {
                ScanAssemblies(services, options.AssembliesToScan, options.Configuration);
            }

            return services;
        }

        private static void ScanAssemblies(
            IServiceCollection services,
            System.Collections.Generic.IEnumerable<Assembly> assemblies,
            Microsoft.Extensions.Configuration.IConfiguration? configuration)
        {
            var allTypes = assemblies.SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && !t.IsInterface && (t.IsPublic || t.IsNestedPublic))
                .ToList();

            foreach (var type in allTypes)
            {
                // Register ICommandHandler<TCommand>
                foreach (var iface in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<>)))
                {
                    if (!services.Any(s => s.ServiceType == iface && s.ImplementationType == type))
                    {
                        services.AddTransient(iface, type);
                    }
                }

                // Register ICommandHandler<TCommand, TResult>
                foreach (var iface in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))
                {
                    if (!services.Any(s => s.ServiceType == iface && s.ImplementationType == type))
                    {
                        services.AddTransient(iface, type);
                    }
                }

                // Register IQueryHandler<TQuery, TResult>
                foreach (var iface in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
                {
                    if (!services.Any(s => s.ServiceType == iface && s.ImplementationType == type))
                    {
                        services.AddTransient(iface, type);
                    }
                }

                // Register IEventHandler<TEvent>
                foreach (var iface in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>)))
                {
                    if (!services.Any(s => s.ServiceType == iface && s.ImplementationType == type))
                    {
                        services.AddTransient(iface, type);
                    }
                }

                // Register IModule
                if (typeof(IModule).IsAssignableFrom(type))
                {
                    if (!services.Any(s => s.ServiceType == typeof(IModule) && (s.ImplementationType == type || s.ImplementationInstance?.GetType() == type)))
                    {
                        if (type.GetConstructor(Type.EmptyTypes) != null)
                        {
                            var moduleInstance = (IModule?)Activator.CreateInstance(type);
                            if (moduleInstance != null)
                            {
                                services.AddSingleton(typeof(IModule), moduleInstance);
                                if (configuration != null)
                                {
                                    moduleInstance.ConfigureServices(services, configuration);
                                }
                            }
                        }
                        else
                        {
                            services.AddSingleton(typeof(IModule), type);
                        }
                    }
                }
            }
        }
    }
}
