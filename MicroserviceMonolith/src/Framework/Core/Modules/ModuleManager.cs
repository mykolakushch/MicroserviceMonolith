using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Modules
{
    /// <summary>
    /// Manages the lifecycle of modules within the application, including registration and shutdown.
    /// </summary>
    public class ModuleManager
    {
        /// <summary>
        /// A list that holds the registered modules.
        /// </summary>
        private readonly List<IModule> _modules = new();

        /// <summary>
        /// Registers modules from the specified assemblies and initializes them asynchronously.
        /// It scans all the types within the given assemblies, looking for types that implement <see cref="IModule"/>.
        /// </summary>
        /// <param name="assemblies">The assemblies from which to load module types.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task RegisterModulesAsync(params Assembly[] assemblies)
        {
            // Select types that implement IModule and are not abstract.
            var moduleTypes = assemblies.SelectMany(a => a.GetTypes())
                .Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract);
            
            // Instantiate each module and initialize it asynchronously.
            foreach (var type in moduleTypes)
            {
                if (Activator.CreateInstance(type) is IModule module)
                {
                    _modules.Add(module); // Add the module to the list.
                    await module.InitializeAsync(); // Initialize the module asynchronously.
                }
            }
        }

        /// <summary>
        /// Shuts down all registered modules asynchronously by calling their <see cref="IModule.ShutdownAsync"/> method.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task ShutdownModulesAsync()
        {
            // Shutdown each registered module asynchronously.
            foreach (var module in _modules)
            {
                await module.ShutdownAsync();
            }
        }
    }
}
