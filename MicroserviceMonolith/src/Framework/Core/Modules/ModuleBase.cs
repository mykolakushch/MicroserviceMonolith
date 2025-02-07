using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Modules
{
    /// <summary>
    /// An abstract base class that implements the <see cref="IModule"/> interface.
    /// Provides default implementations for module initialization and shutdown methods.
    /// </summary>
    public abstract class ModuleBase : IModule
    {
        /// <summary>
        /// Asynchronously initializes the module.
        /// Provides a default no-op implementation. Derived classes can override this method to provide specific initialization logic.
        /// </summary>
        /// <returns>A task representing the asynchronous initialization process.</returns>
        public virtual Task InitializeAsync() => Task.CompletedTask;

        /// <summary>
        /// Asynchronously shuts down the module.
        /// Provides a default no-op implementation. Derived classes can override this method to provide specific shutdown logic.
        /// </summary>
        /// <returns>A task representing the asynchronous shutdown process.</returns>
        public virtual Task ShutdownAsync() => Task.CompletedTask;
    }
}
