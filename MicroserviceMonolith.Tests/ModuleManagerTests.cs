using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Modules;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class ModuleManagerTests
    {
        private class TestModuleA : ModuleBase
        {
            private readonly List<string> _events;

            public TestModuleA(List<string> events) => _events = events;

            public override Task InitializeAsync(IServiceProvider sp, CancellationToken ct)
            {
                _events.Add("A:Init");
                return Task.CompletedTask;
            }

            public override Task ShutdownAsync(IServiceProvider sp, CancellationToken ct)
            {
                _events.Add("A:Shutdown");
                return Task.CompletedTask;
            }
        }

        private class TestModuleB : ModuleBase
        {
            private readonly List<string> _events;

            public TestModuleB(List<string> events) => _events = events;

            public override Task InitializeAsync(IServiceProvider sp, CancellationToken ct)
            {
                _events.Add("B:Init");
                return Task.CompletedTask;
            }

            public override Task ShutdownAsync(IServiceProvider sp, CancellationToken ct)
            {
                _events.Add("B:Shutdown");
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task ModuleManager_ExecutesInitInOrder_AndShutdownInReverseOrder()
        {
            var manager = new ModuleManager();
            var events = new List<string>();

            var moduleA = new TestModuleA(events);
            var moduleB = new TestModuleB(events);

            manager.AddModule(moduleA);
            manager.AddModule(moduleB);

            var services = new ServiceCollection().BuildServiceProvider();

            await manager.InitializeModulesAsync(services);
            events.Should().Equal("A:Init", "B:Init");

            await manager.ShutdownModulesAsync(services);
            events.Should().Equal("A:Init", "B:Init", "B:Shutdown", "A:Shutdown");
        }

        [Fact]
        public void ConfigureModules_RegistersDiscoveredModules()
        {
            var manager = new ModuleManager();
            var services = new ServiceCollection();
            var config = new ConfigurationBuilder().Build();

            // Calling assembly scanning
            manager.ConfigureModules(services, config, typeof(ModuleManagerTests).Assembly);

            manager.Modules.Should().NotBeEmpty();
        }
    }
}
