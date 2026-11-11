using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Common;
using Microsoft.Extensions.Logging;

namespace MicroserviceMonolith.Core.Modules
{
    /// <summary>
    /// Manages discovery, service configuration, and graceful lifecycle (initialization and shutdown) of modules.
    /// </summary>
    public class ModuleManager
    {
        private readonly List<IModule> _modules = new();
        private readonly HashSet<IModule> _initialized = new();
        private readonly object _lock = new();
        private readonly ILogger? _logger;

        /// <summary>
        /// Initializes a new instance of <see cref="ModuleManager"/>.
        /// </summary>
        /// <param name="logger">Optional logger used to report module shutdown failures.</param>
        public ModuleManager(ILogger? logger = null)
        {
            _logger = logger;
        }

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
        /// Discovers module classes (public or not, abstract and open generic types excluded) from assemblies and invokes their <see cref="IModule.ConfigureServices"/> methods.
        /// Registers the discovered modules as singletons in the service collection.
        /// </summary>
        public void ConfigureModules(IServiceCollection services, IConfiguration configuration, params Assembly[] assemblies)
        {
            if (assemblies == null || assemblies.Length == 0)
            {
                assemblies = new[] { Assembly.GetCallingAssembly() };
            }

            var created = new List<IModule>();
            foreach (var type in DiscoverModuleTypes(assemblies))
            {
                // Instantiate module using default constructor if available
                if (type.GetConstructor(Type.EmptyTypes) != null)
                {
                    if (Activator.CreateInstance(type) is IModule instance)
                    {
                        created.Add(instance);
                    }
                }
                else
                {
                    services.AddSingleton(typeof(IModule), type);
                }
            }

            // Configure dependencies first (see ModuleDependsOnAttribute)
            foreach (var instance in ModuleDependencyOrder.Sort(created, requireAllDependencies: false))
            {
                AddModule(instance);
                services.AddSingleton(typeof(IModule), instance);
                instance.ConfigureServices(services, configuration);
            }
        }

        private static List<Type> DiscoverModuleTypes(Assembly[] assemblies)
        {
            return AssemblyScanning.GetOrderedTypes(assemblies)
                .Where(t => typeof(IModule).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters)
                .ToList();
        }

        /// <summary>
        /// Initializes all registered modules using the provided <see cref="IServiceProvider"/>, dependencies
        /// (see <see cref="ModuleDependsOnAttribute"/>) first.
        /// Also discovers any <see cref="IModule"/> registered inside the DI container.
        /// Each module is initialized at most once until <see cref="ShutdownModulesAsync"/> runs, so calling this
        /// method manually in addition to the automatic hosted service does not initialize modules twice.
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
                // Dependencies first; modules without dependencies keep their registration order
                modulesToInit = ModuleDependencyOrder.Sort(_modules, requireAllDependencies: true)
                    .Where(m => !_initialized.Contains(m))
                    .ToList();
            }

            foreach (var module in modulesToInit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await module.InitializeAsync(serviceProvider, cancellationToken);
                lock (_lock)
                {
                    _initialized.Add(module);
                }
            }
        }

        /// <summary>
        /// Shuts down the initialized modules in reverse order of initialization (dependents before their dependencies). Modules that were never initialized are skipped.
        /// </summary>
        public async Task ShutdownModulesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));

            List<IModule> modulesToShutdown;
            lock (_lock)
            {
                try
                {
                    modulesToShutdown = ModuleDependencyOrder.Sort(_modules, requireAllDependencies: false);
                }
                catch (InvalidOperationException)
                {
                    // Invalid dependency graph: shutdown must still run, fall back to registration order.
                    modulesToShutdown = _modules.ToList();
                }

                // Only modules that were actually initialized are shut down (a module whose startup never ran,
                // or failed, has nothing to release).
                modulesToShutdown = modulesToShutdown.Where(m => _initialized.Contains(m)).ToList();
            }

            // Shutdown in reverse (LIFO) order, so dependents stop before the modules they depend on
            modulesToShutdown.Reverse();

            foreach (var module in modulesToShutdown)
            {
                try
                {
                    await module.ShutdownAsync(serviceProvider, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Continue shutting down remaining modules even if one fails, but do not hide the failure
                    _logger?.LogError(ex, "[ModuleManager] Shutdown of module '{Module}' failed", module.Name);
                }
            }

            lock (_lock)
            {
                // Allow modules to be initialized again after a full shutdown (e.g. host restart).
                _initialized.Clear();
            }
        }
    }
}
