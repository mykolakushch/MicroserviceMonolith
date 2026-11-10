using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MicroserviceMonolith.Core.Modules
{
    /// <summary>
    /// Stable topological ordering of modules by <see cref="ModuleDependsOnAttribute"/>.
    /// Modules without dependencies keep their original relative order.
    /// </summary>
    internal static class ModuleDependencyOrder
    {
        private const int InProgress = 1;
        private const int Done = 2;

        public static List<IModule> Sort(IReadOnlyList<IModule> modules, bool requireAllDependencies)
        {
            var result = new List<IModule>(modules.Count);
            var state = new Dictionary<IModule, int>(ReferenceEqualityComparer.Instance);
            var path = new List<IModule>();

            foreach (var module in modules)
            {
                Visit(module);
            }

            return result;

            void Visit(IModule module)
            {
                if (state.TryGetValue(module, out var current))
                {
                    if (current == Done)
                    {
                        return;
                    }

                    var cycle = path.SkipWhile(m => !ReferenceEquals(m, module)).Append(module).Select(m => m.Name);
                    throw new InvalidOperationException($"Circular module dependency: {string.Join(" -> ", cycle)}.");
                }

                state[module] = InProgress;
                path.Add(module);

                foreach (var dependencyType in GetDependencies(module))
                {
                    var dependencies = modules.Where(m => dependencyType.IsInstanceOfType(m)).ToList();
                    if (dependencies.Count == 0)
                    {
                        if (requireAllDependencies)
                        {
                            throw new InvalidOperationException(
                                $"Module '{module.Name}' depends on '{dependencyType.Name}', which is not registered.");
                        }
                        continue;
                    }

                    foreach (var dependency in dependencies)
                    {
                        Visit(dependency);
                    }
                }

                path.RemoveAt(path.Count - 1);
                state[module] = Done;
                result.Add(module);
            }
        }

        private static IEnumerable<Type> GetDependencies(IModule module)
        {
            return module.GetType()
                .GetCustomAttributes<ModuleDependsOnAttribute>(inherit: false)
                .SelectMany(a => a.ModuleTypes);
        }
    }
}
