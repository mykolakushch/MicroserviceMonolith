using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using MicroserviceMonolith.Core.Execution;

namespace MicroserviceMonolith.Core.DependencyInjection
{
    /// <summary>
    /// Configuration builder for configuring MicroserviceMonolith framework services and pipelines.
    /// </summary>
    public class MicroserviceMonolithOptions
    {
        public List<Assembly> AssembliesToScan { get; } = new();
        public bool EnableOutbox { get; set; } = true;

        /// <summary>
        /// When true (default) and the outbox is enabled, a hosted service polls the outbox and dispatches pending
        /// messages while the host runs. Set to false if you drive <c>IOutboxProcessor</c> yourself.
        /// </summary>
        public bool AutoProcessOutbox { get; set; } = true;

        /// <summary>
        /// Pause between outbox polls of the background service. Default: 1 second.
        /// </summary>
        public TimeSpan OutboxPollingInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Maximum number of outbox messages handled per batch by the background service. Default: 50.
        /// </summary>
        public int OutboxBatchSize { get; set; } = 50;

        /// <summary>
        /// When true (default), a hosted service initializes modules when the host starts and shuts them down when
        /// it stops. Set to false if you call <c>ModuleManager.InitializeModulesAsync</c>/<c>ShutdownModulesAsync</c> yourself.
        /// </summary>
        public bool AutoManageModuleLifecycle { get; set; } = true;
        public IConfiguration Configuration { get; set; } = new ConfigurationBuilder().Build();
        public ExecutionQueueOptions ExecutionQueueOptions { get; set; } = new();

        /// <summary>
        /// Adds assemblies to scan for modules, handlers, and middlewares.
        /// </summary>
        public MicroserviceMonolithOptions RegisterServicesFromAssemblies(params Assembly[] assemblies)
        {
            if (assemblies != null)
            {
                foreach (var assembly in assemblies)
                {
                    if (!AssembliesToScan.Contains(assembly))
                    {
                        AssembliesToScan.Add(assembly);
                    }
                }
            }
            return this;
        }

        /// <summary>
        /// Adds the assembly containing the specified marker type.
        /// </summary>
        public MicroserviceMonolithOptions RegisterServicesFromAssemblyContaining<T>()
        {
            return RegisterServicesFromAssemblies(typeof(T).Assembly);
        }
    }
}
