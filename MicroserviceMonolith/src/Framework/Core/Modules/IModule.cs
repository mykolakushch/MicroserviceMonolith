using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MicroserviceMonolith.Core.Modules
{
    /// <summary>
    /// Defines the contract for an encapsulated business module in the modular monolith architecture.
    /// Each module can configure its own private services and participate in application lifecycle events.
    /// </summary>
    public interface IModule
    {
        /// <summary>
        /// Unique name of the module.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Configures services, repositories, and dependencies specific to this module into the DI container.
        /// </summary>
        /// <param name="services">The application service collection.</param>
        /// <param name="configuration">The application configuration root.</param>
        void ConfigureServices(IServiceCollection services, IConfiguration configuration);

        /// <summary>
        /// Asynchronously initializes the module after the service provider is built.
        /// </summary>
        /// <param name="serviceProvider">The root service provider.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task representing the initialization operation.</returns>
        Task InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously shuts down the module when the application is stopping.
        /// </summary>
        /// <param name="serviceProvider">The root service provider.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task representing the shutdown operation.</returns>
        Task ShutdownAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default);

        /// <summary>
        /// Legacy parameterless initialization.
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Legacy parameterless shutdown.
        /// </summary>
        Task ShutdownAsync();
    }
}
