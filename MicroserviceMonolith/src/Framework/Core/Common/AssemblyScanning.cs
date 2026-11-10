using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MicroserviceMonolith.Core.Common
{
    internal static class AssemblyScanning
    {
        /// <summary>
        /// Returns the types of an assembly that can be loaded. If some types fail to load
        /// (for example because an optional dependency is missing) the rest are still returned
        /// instead of the whole scan failing with <see cref="ReflectionTypeLoadException"/>.
        /// </summary>
        public static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null)!;
            }
        }

        /// <summary>
        /// Distinct types of all assemblies in a deterministic order (by full name), so that
        /// registration order does not depend on reflection or assembly order.
        /// </summary>
        public static List<Type> GetOrderedTypes(IEnumerable<Assembly> assemblies)
        {
            return assemblies
                .Distinct()
                .SelectMany(GetLoadableTypes)
                .Distinct()
                .OrderBy(t => t.FullName ?? t.Name, StringComparer.Ordinal)
                .ToList();
        }
    }
}
