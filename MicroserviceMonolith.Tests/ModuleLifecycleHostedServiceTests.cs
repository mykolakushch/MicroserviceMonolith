using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MicroserviceMonolith.Core.DependencyInjection;
using MicroserviceMonolith.Core.Modules;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class ModuleLifecycleHostedServiceTests
    {
        private sealed class RecordingModule : ModuleBase
        {
            private readonly string _label;
            private readonly List<string> _events;

            public RecordingModule(string label, List<string> events)
            {
                _label = label;
                _events = events;
            }

            public override Task InitializeAsync(IServiceProvider sp, CancellationToken ct)
            {
                _events.Add($"{_label}:Init");
                return Task.CompletedTask;
            }

            public override Task ShutdownAsync(IServiceProvider sp, CancellationToken ct)
            {
                _events.Add($"{_label}:Shutdown");
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task HostedService_InitializesModulesOnStart_AndShutsDownInReverseOrderOnStop()
        {
            var events = new List<string>();
            var services = new ServiceCollection();
            services.AddSingleton<IModule>(new RecordingModule("A", events));
            services.AddSingleton<IModule>(new RecordingModule("B", events));
            services.AddMicroserviceMonolith();

            using var sp = services.BuildServiceProvider();
            var hosted = sp.GetServices<IHostedService>().OfType<ModuleLifecycleHostedService>().Single();

            await hosted.StartAsync(CancellationToken.None);
            events.Should().Equal("A:Init", "B:Init");

            await hosted.StopAsync(CancellationToken.None);
            events.Should().Equal("A:Init", "B:Init", "B:Shutdown", "A:Shutdown");
        }

        [Fact]
        public void AddMicroserviceMonolith_WithAutoManageDisabled_DoesNotRegisterHostedService()
        {
            var services = new ServiceCollection();

            services.AddMicroserviceMonolith(options => options.AutoManageModuleLifecycle = false);

            services.Any(d => d.ImplementationType == typeof(ModuleLifecycleHostedService)).Should().BeFalse();
        }

        [Fact]
        public void AddMicroserviceMonolith_CalledTwice_RegistersHostedServiceOnce()
        {
            var services = new ServiceCollection();

            services.AddMicroserviceMonolith();
            services.AddMicroserviceMonolith();

            services.Count(d =>
                    d.ServiceType == typeof(IHostedService) &&
                    d.ImplementationType == typeof(ModuleLifecycleHostedService))
                .Should().Be(1);
        }

        [Fact]
        public async Task ModuleManager_InitializeCalledTwice_InitializesEachModuleOnce_AndAllowsRestartAfterShutdown()
        {
            var events = new List<string>();
            var manager = new ModuleManager();
            manager.AddModule(new RecordingModule("A", events));
            var sp = new ServiceCollection().BuildServiceProvider();

            await manager.InitializeModulesAsync(sp);
            await manager.InitializeModulesAsync(sp); // e.g. manual call on top of the hosted service
            events.Should().Equal("A:Init");

            await manager.ShutdownModulesAsync(sp);
            await manager.InitializeModulesAsync(sp);
            events.Should().Equal("A:Init", "A:Shutdown", "A:Init");
        }
    }
}
