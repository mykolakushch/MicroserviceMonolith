using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MicroserviceMonolith.Core.Modules
{
    /// <summary>
    /// Manages discovery, service configuration, and graceful lifecycle (initialization and shutdown) of modules.
    /// </summary>
    public class ModuleManager
    {
        private readonly List<IModule> _modules = new();
        private readonly object _lock = new();

        /// <summary>
        /// Gets a read-only list of all registered modules.
        /// </summary>
        public IReadOnlyList<IModule> Modules
        {
            get
            {
                lock (_lock)
                {
                    return _modules.ToList();
                }
            }
        }

        /// <summary>
        /// Adds an already instantiated module instance.
        /// </summary>
        public void AddModule(IModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            lock (_lock)
            {
                if (!_modules.Contains(module))
                {
                    _modules.Add(module);
                }
            }
        }

        /// <summary>
        /// Discovers module types from assemblies and invokes their <see cref="IModule.ConfigureServices"/> methods.
        /// Registers the discovered modules as singletons in the service collection.
        /// </summary>
        public void ConfigureModules(IServiceCollection services, IConfiguration configuration, params Assembly[] assemblies)
        {
            if (assemblies == null || assemblies.Length == 0)
            {
                assemblies = new[] { Assembly.GetCallingAssembly() };
            }

            var moduleTypes = assemblies.SelectMany(a => a.GetTypes())
                .Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface && (t.IsPublic || t.IsNestedPublic));

            foreach (var type in moduleTypes)
            {
                // Instantiate module using default constructor if available
                if (type.GetConstructor(Type.EmptyTypes) != null)
                {
                    var instance = (IModule?)Activator.CreateInstance(type);
                    if (instance != null)
                    {
                        AddModule(instance);
                        services.AddSingleton(typeof(IModule), instance);
                        instance.ConfigureServices(services, configuration);
                    }
                }
                else
                {
                    services.AddSingleton(typeof(IModule), type);
                }
            }
        }

        /// <summary>
        /// Legacy method: scans assemblies, creates instances, and calls <see cref="IModule.InitializeAsync()"/>.
        /// </summary>
        public async Task RegisterModulesAsync(params Assembly[] assemblies)
        {
            if (assemblies == null || assemblies.Length == 0)
            {
                assemblies = new[] { Assembly.GetCallingAssembly() };
            }

            var moduleTypes = assemblies.SelectMany(a => a.GetTypes())
                .Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface && (t.IsPublic || t.IsNestedPublic));

            foreach (var type in moduleTypes)
            {
                if (type.GetConstructor(Type.EmptyTypes) != null && Activator.CreateInstance(type) is IModule module)
                {
                    AddModule(module);
                    await module.InitializeAsync();
                }
            }
        }

        /// <summary>
        /// Initializes all registered modules using the provided <see cref="IServiceProvider"/>.
        /// Also discovers any <see cref="IModule"/> registered inside the DI container.
        /// </summary>
        public async Task InitializeModulesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));

            // Pull any modules registered in DI
            var diModules = serviceProvider.GetServices<IModule>();
            foreach (var diModule in diModules)
            {
                AddModule(diModule);
            }

            List<IModule> modulesToInit;
            lock (_lock)
            {
                modulesToInit = _modules.ToList();
            }

            foreach (var module in modulesToInit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await module.InitializeAsync(serviceProvider, cancellationToken);
            }
        }

        /// <summary>
        /// Shuts down all registered modules in reverse order of initialization.
        /// </summary>
        public async Task ShutdownModulesAsync(IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
        {
            List<IModule> modulesToShutdown;
            lock (_lock)
            {
                modulesToShutdown = _modules.ToList();
            }

            // Shutdown in reverse (LIFO) order
            modulesToShutdown.Reverse();

            foreach (var module in modulesToShutdown)
            {
                try
                {
                    if (serviceProvider != null)
                    {
                        await module.ShutdownAsync(serviceProvider, cancellationToken);
                    }
                    else
                    {
                        await module.ShutdownAsync();
                    }
                }
                catch
                {
                    // Continue shutting down remaining modules even if one fails
                }
            }
        }
    }
}
