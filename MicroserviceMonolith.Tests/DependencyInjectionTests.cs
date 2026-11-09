using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Commands;
using MicroserviceMonolith.Core.DependencyInjection;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Core.Execution;
using MicroserviceMonolith.Core.Modules;
using MicroserviceMonolith.Core.Outbox;
using MicroserviceMonolith.Core.Queries;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class DependencyInjectionTests
    {
        public record EchoCommand(string Text) : ICommand<string>;

        public class EchoCommandHandler : ICommandHandler<EchoCommand, string>
        {
            public Task<string> HandleAsync(EchoCommand command, CancellationToken cancellationToken = default)
            {
                return Task.FromResult($"Echo: {command.Text}");
            }
        }

        public class SampleScannedModule : ModuleBase
        {
            public override string Name => "SampleScanned";
        }

        [Fact]
        public async Task AddMicroserviceMonolith_RegistersAllCoreServicesAndScansHandlers()
        {
            var services = new ServiceCollection();

            services.AddMicroserviceMonolith(options =>
            {
                options.RegisterServicesFromAssemblies(typeof(DependencyInjectionTests).Assembly);
            });

            var sp = services.BuildServiceProvider();

            // Verify core singletons
            sp.GetService<ICommandBus>().Should().NotBeNull();
            sp.GetService<IQueryBus>().Should().NotBeNull();
            sp.GetService<IEventPublisher>().Should().NotBeNull();
            sp.GetService<IEventSubscriber>().Should().NotBeNull();
            sp.GetService<ExecutionQueue>().Should().NotBeNull();
            sp.GetService<ModuleManager>().Should().NotBeNull();
            sp.GetService<IOutboxStore>().Should().NotBeNull();
            sp.GetService<IOutboxProcessor>().Should().NotBeNull();

            // Verify scanned handler
            var commandBus = sp.GetRequiredService<ICommandBus>();
            var result = await commandBus.SendAsync<EchoCommand, string>(new EchoCommand("Testing DI"));
            result.Should().Be("Echo: Testing DI");

            // Verify scanned module
            var moduleManager = sp.GetRequiredService<ModuleManager>();
            await moduleManager.InitializeModulesAsync(sp);
            moduleManager.Modules.Should().Contain(m => m.Name == "SampleScanned");
        }
    }
}
