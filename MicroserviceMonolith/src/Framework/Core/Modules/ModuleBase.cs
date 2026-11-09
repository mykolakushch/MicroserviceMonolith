using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MicroserviceMonolith.Core.Modules
{
    /// <summary>
    /// Abstract base class for application modules providing default virtual no-op lifecycle implementations.
    /// </summary>
    public abstract class ModuleBase : IModule
    {
        /// <inheritdoc />
        public virtual string Name => GetType().Name;

        /// <inheritdoc />
        public virtual void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
        }

        /// <inheritdoc />
        public virtual Task InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            return InitializeAsync();
        }

        /// <inheritdoc />
        public virtual Task ShutdownAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            return ShutdownAsync();
        }

        /// <inheritdoc />
        public virtual Task InitializeAsync() => Task.CompletedTask;

        /// <inheritdoc />
        public virtual Task ShutdownAsync() => Task.CompletedTask;
    }
}
