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
