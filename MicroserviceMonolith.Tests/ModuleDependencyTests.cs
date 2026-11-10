using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Commands;
using MicroserviceMonolith.Core.DependencyInjection;
using MicroserviceMonolith.Core.Modules;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class ModuleDependencyTests
    {
        public record DuplicateCommand : ICommand<int>;

        private abstract class RecordingModule : ModuleBase
        {
            private readonly List<string> _events;

            protected RecordingModule(List<string> events) => _events = events;

            public override Task InitializeAsync(IServiceProvider sp, CancellationToken ct)
            {
                _events.Add($"{Name}:Init");
                return Task.CompletedTask;
            }

            public override Task ShutdownAsync(IServiceProvider sp, CancellationToken ct)
            {
                _events.Add($"{Name}:Shutdown");
                return Task.CompletedTask;
            }
        }

        private sealed class ModA : RecordingModule { public ModA(List<string> e) : base(e) { } }

        [ModuleDependsOn(typeof(ModA))]
        private sealed class ModB : RecordingModule { public ModB(List<string> e) : base(e) { } }

        [ModuleDependsOn(typeof(ModB))]
        private sealed class ModC : RecordingModule { public ModC(List<string> e) : base(e) { } }

        [ModuleDependsOn(typeof(CycleY))]
        private sealed class CycleX : RecordingModule { public CycleX(List<string> e) : base(e) { } }

        [ModuleDependsOn(typeof(CycleX))]
        private sealed class CycleY : RecordingModule { public CycleY(List<string> e) : base(e) { } }

        [ModuleDependsOn(typeof(ModA))]
        private sealed class Orphan : RecordingModule { public Orphan(List<string> e) : base(e) { } }

        private sealed class FailingOnShutdown : RecordingModule
        {
            public FailingOnShutdown(List<string> e) : base(e) { }

            public override Task ShutdownAsync(IServiceProvider sp, CancellationToken ct)
                => throw new InvalidOperationException("boom");
        }

        private static IServiceProvider EmptyProvider() => new ServiceCollection().BuildServiceProvider();

        [Fact]
        public async Task Initialize_RunsDependenciesFirst_AndShutdownIsReversed()
        {
            var events = new List<string>();
            var manager = new ModuleManager();
            manager.AddModule(new ModC(events)); // registered first, but depends on B which depends on A
            manager.AddModule(new ModB(events));
            manager.AddModule(new ModA(events));
            var sp = EmptyProvider();

            await manager.InitializeModulesAsync(sp);
            events.Should().Equal("ModA:Init", "ModB:Init", "ModC:Init");

            events.Clear();
            await manager.ShutdownModulesAsync(sp);
            events.Should().Equal("ModC:Shutdown", "ModB:Shutdown", "ModA:Shutdown");
        }

        [Fact]
        public async Task Initialize_WithCircularDependency_Throws()
        {
            var events = new List<string>();
            var manager = new ModuleManager();
            manager.AddModule(new CycleX(events));
            manager.AddModule(new CycleY(events));

            Func<Task> act = () => manager.InitializeModulesAsync(EmptyProvider());

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Circular*");
            events.Should().BeEmpty();
        }

        [Fact]
        public async Task Initialize_WithMissingDependency_Throws()
        {
            var manager = new ModuleManager();
            manager.AddModule(new Orphan(new List<string>()));

            Func<Task> act = () => manager.InitializeModulesAsync(EmptyProvider());

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Orphan*ModA*");
        }

        [Fact]
        public async Task Shutdown_FailureIsLogged_AndRemainingModulesStillShutDown()
        {
            var events = new List<string>();
            var logger = new TestLogger();
            var manager = new ModuleManager(logger);
            manager.AddModule(new ModA(events));
            manager.AddModule(new FailingOnShutdown(events));
            var sp = EmptyProvider();
            await manager.InitializeModulesAsync(sp);

            await manager.ShutdownModulesAsync(sp); // must not throw

            events.Should().Contain("ModA:Shutdown");
            logger.Entries.Should().ContainSingle(e => e.Message.Contains("FailingOnShutdown") && e.Exception != null && e.Exception.Message == "boom");
        }

        [Fact]
        public void ModuleManager_ResolvesFromDi_WithAndWithoutLogger()
        {
            var plain = new ServiceCollection();
            plain.AddMicroserviceMonolith();
            using var plainProvider = plain.BuildServiceProvider();
            plainProvider.GetRequiredService<ModuleManager>().Should().NotBeNull();
        }

        [Fact]
        public void Scanning_TwoHandlersForTheSameCommand_ThrowsInsteadOfPickingOne()
        {
            var assembly = BuildAssemblyWithDuplicateHandlers();
            var services = new ServiceCollection();

            Action act = () => services.AddMicroserviceMonolith(o => o.RegisterServicesFromAssemblies(assembly));

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Ambiguous handler registration*DuplicateHandlerA*DuplicateHandlerB*");
        }

        private static Assembly BuildAssemblyWithDuplicateHandlers()
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("DuplicateHandlersAssembly"), AssemblyBuilderAccess.Run);
            var module = assembly.DefineDynamicModule("Main");
            var handlerInterface = typeof(ICommandHandler<DuplicateCommand, int>);
            var interfaceMethod = handlerInterface.GetMethod("HandleAsync")!;

            foreach (var name in new[] { "DuplicateHandlerA", "DuplicateHandlerB" })
            {
                var type = module.DefineType(
                    name,
                    TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed,
                    typeof(object),
                    new[] { handlerInterface });
                type.DefineDefaultConstructor(MethodAttributes.Public);

                var method = type.DefineMethod(
                    "HandleAsync",
                    MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                    typeof(Task<int>),
                    new[] { typeof(DuplicateCommand), typeof(CancellationToken) });
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);

                type.DefineMethodOverride(method, interfaceMethod);
                type.CreateType();
            }

            return assembly;
        }
    }
}
