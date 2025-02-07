using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Modules
{
    /// <summary>
    /// Defines the contract for a module in the microservice monolith architecture.
    /// Modules are responsible for initialization and shutdown operations.
    /// </summary>
    public interface IModule
    {
        /// <summary>
        /// Asynchronously initializes the module.
        /// This method is used to set up any required resources or configurations before the module starts working.
        /// </summary>
        /// <returns>A task representing the asynchronous initialization process.</returns>
        Task InitializeAsync();

        /// <summary>
        /// Asynchronously shuts down the module.
        /// This method is used to clean up resources or perform any shutdown operations when the module is no longer needed.
        /// </summary>
        /// <returns>A task representing the asynchronous shutdown process.</returns>
        Task ShutdownAsync();
    }
}
