using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MicroserviceMonolith.Core.Commands;
using MicroserviceMonolith.Core.Common;
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

            // Execution Queue with correlation tracking
            services.TryAddSingleton(sp => new ExecutionQueue(
                options.ExecutionQueueOptions,
                sp.GetService<ICorrelationContextAccessor>(),
                (msg, ex) => sp.GetService<ILogger>()?.Log($"[ExecutionQueue] {msg}: {ex.Message}")));

            // Module Manager
            services.TryAddSingleton<ModuleManager>();

            // Tie module initialization/shutdown to the host lifetime
            if (options.AutoManageModuleLifecycle)
            {
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ModuleLifecycleHostedService>());
            }

            // Command Bus with correlation tracking
            services.TryAddSingleton<ICommandBus>(sp => new CommandBus(sp, sp.GetService<ICorrelationContextAccessor>()));

            // Query Bus with correlation tracking
            services.TryAddSingleton<IQueryBus>(sp => new QueryBus(sp, sp.GetService<ICorrelationContextAccessor>()));

            // Event Bus
            services.TryAddSingleton<EventBus>(sp => new EventBus(sp));
            services.TryAddSingleton<IEventPublisher>(sp => sp.GetRequiredService<EventBus>());
            services.TryAddSingleton<IEventSubscriber>(sp => sp.GetRequiredService<EventBus>());

            // Outbox
            if (options.EnableOutbox)
            {
                services.TryAddSingleton<IOutboxStore, InMemoryOutboxStore>();
                // The processor resolves the store from a new scope per batch, so IOutboxStore may be scoped
                // (for example a database-backed store sharing the request's unit of work).
                services.TryAddSingleton<IOutboxProcessor>(sp => new OutboxProcessor(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<IEventPublisher>(),
                    sp.GetService<ICorrelationContextAccessor>(),
                    sp.GetService<ILogger>() != null ? sp.GetRequiredService<ILogger>().Log : null));
                services.TryAddScoped<IOutboxWriter>(sp => new OutboxWriter(
                    sp.GetRequiredService<IOutboxStore>(),
                    sp.GetService<ICorrelationContextAccessor>()));

                if (options.AutoProcessOutbox)
                {
                    services.TryAddSingleton(new OutboxProcessingOptions
                    {
                        PollingInterval = options.OutboxPollingInterval,
                        BatchSize = options.OutboxBatchSize
                    });
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxProcessorHostedService>());
                }
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
            IEnumerable<Assembly> assemblies,
            IConfiguration configuration)
        {
            var allTypes = assemblies.SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && !t.IsInterface)
                .ToList();

            var registeredModuleTypes = new HashSet<Type>(
                services.Where(s => s.ServiceType == typeof(IModule))
                        .Select(s => s.ImplementationType ?? s.ImplementationInstance?.GetType())
                        .Where(t => t != null)!);

            // Phase 1: Discover and configure all modules first so their custom services (including factories) are registered
            foreach (var type in allTypes.Where(t => typeof(IModule).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null))
            {
                if (registeredModuleTypes.Add(type))
                {
                    var moduleInstance = (IModule?)Activator.CreateInstance(type);
                    if (moduleInstance != null)
                    {
                        services.AddSingleton(typeof(IModule), moduleInstance);
                        moduleInstance.ConfigureServices(services, configuration);
                    }
                }
            }

            // Phase 2: Index all already-registered single-handler interfaces (from factory, instance, or type) in O(1) set
            var registeredSingleHandlerTypes = new HashSet<Type>(
                services.Where(s =>
                    s.ServiceType.IsGenericType &&
                    (s.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<>) ||
                     s.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) ||
                     s.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
                    .Select(s => s.ServiceType));

            // Index (ServiceType, ImplementationType) pairs to prevent duplicate event handler registrations
            var registeredDescriptors = new HashSet<(Type ServiceType, Type ImplementationType)>(
                services.Where(s => s.ImplementationType != null)
                        .Select(s => (s.ServiceType, s.ImplementationType!)));

            // Phase 3: Auto-scan remaining handlers without duplicate registrations
            foreach (var type in allTypes)
            {
                // Register ICommandHandler<TCommand> and ICommandHandler<TCommand, Unit>
                foreach (var iface in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<>)))
                {
                    if (registeredSingleHandlerTypes.Add(iface) && registeredDescriptors.Add((iface, type)))
                    {
                        services.AddTransient(iface, type);

                        // Register zero-reflection Unit handler
                        var cmdType = iface.GetGenericArguments()[0];
                        var unitHandlerType = typeof(ICommandHandler<,>).MakeGenericType(cmdType, typeof(Unit));
                        if (registeredSingleHandlerTypes.Add(unitHandlerType) && registeredDescriptors.Add((unitHandlerType, type)))
                        {
                            services.AddTransient(unitHandlerType, type);
                        }
                    }
                }

                // Register ICommandHandler<TCommand, TResult>
                foreach (var iface in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))
                {
                    if (registeredSingleHandlerTypes.Add(iface) && registeredDescriptors.Add((iface, type)))
                    {
                        services.AddTransient(iface, type);
                    }
                }

                // Register IQueryHandler<TQuery, TResult>
                foreach (var iface in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
                {
                    if (registeredSingleHandlerTypes.Add(iface) && registeredDescriptors.Add((iface, type)))
                    {
                        services.AddTransient(iface, type);
                    }
                }

                // Register IEventHandler<TEvent>
                foreach (var iface in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>)))
                {
                    if (registeredDescriptors.Add((iface, type)))
                    {
                        services.AddTransient(iface, type);
                    }
                }
            }
        }
    }
}
