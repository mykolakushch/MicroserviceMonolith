using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Modules
{
    public class ModuleManager
    {
        private readonly List<IModule> _modules = new();

        public async Task RegisterModulesAsync(params Assembly[] assemblies)
        {
            var moduleTypes = assemblies.SelectMany(a => a.GetTypes())
                .Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract);
            foreach (var type in moduleTypes)
            {
                if (Activator.CreateInstance(type) is IModule module)
                {
                    _modules.Add(module);
                    await module.InitializeAsync();
                }
            }
        }

        public async Task ShutdownModulesAsync()
        {
            foreach (var module in _modules)
            {
                await module.ShutdownAsync();
            }
        }
    }
}
