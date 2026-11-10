using System;
using System.Collections.Generic;

namespace MicroserviceMonolith.Core.Modules
{
    /// <summary>
    /// Declares that a module needs other modules to be configured and initialized before it.
    /// Modules are shut down in the reverse order. Circular dependencies, or a dependency on a module
    /// that is not registered, make <see cref="ModuleManager.InitializeModulesAsync"/> throw an <see cref="InvalidOperationException"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class ModuleDependsOnAttribute : Attribute
    {
        public ModuleDependsOnAttribute(params Type[] moduleTypes)
        {
            ModuleTypes = moduleTypes ?? throw new ArgumentNullException(nameof(moduleTypes));
        }

        /// <summary>
        /// The module types (or base types / interfaces of modules) this module depends on.
        /// </summary>
        public IReadOnlyList<Type> ModuleTypes { get; }
    }
}
