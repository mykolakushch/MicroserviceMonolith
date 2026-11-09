using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace MicroserviceMonolith.Core.Modules
{
    /// <summary>
    /// Hosted service that ties the <see cref="ModuleManager"/> lifecycle to the application host:
    /// modules are initialized when the host starts and shut down (in reverse order) when it stops.
    /// Registered automatically by <c>AddMicroserviceMonolith</c> unless
    /// <c>MicroserviceMonolithOptions.AutoManageModuleLifecycle</c> is set to <c>false</c>.
    /// </summary>
    public sealed class ModuleLifecycleHostedService : IHostedService
    {
        private readonly ModuleManager _moduleManager;
        private readonly IServiceProvider _serviceProvider;

        public ModuleLifecycleHostedService(ModuleManager moduleManager, IServiceProvider serviceProvider)
        {
            _moduleManager = moduleManager ?? throw new ArgumentNullException(nameof(moduleManager));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return _moduleManager.InitializeModulesAsync(_serviceProvider, cancellationToken);
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return _moduleManager.ShutdownModulesAsync(_serviceProvider, cancellationToken);
        }
    }
}
