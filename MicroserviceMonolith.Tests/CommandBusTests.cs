using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Commands;
using MicroserviceMonolith.Core.Common;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class CommandBusTests
    {
        public record TestVoidCommand(string Message) : ICommand;
        public record TestResultCommand(int A, int B) : ICommand<int>;

        private class TestVoidCommandHandler : ICommandHandler<TestVoidCommand>
        {
            public bool Handled { get; private set; }
            public string? ReceivedMessage { get; private set; }

            public Task HandleAsync(TestVoidCommand command, CancellationToken cancellationToken = default)
            {
                Handled = true;
                ReceivedMessage = command.Message;
                return Task.CompletedTask;
            }
        }

        private class TestResultCommandHandler : ICommandHandler<TestResultCommand, int>
        {
            public Task<int> HandleAsync(TestResultCommand command, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(command.A + command.B);
            }
        }

        private class OrderTrackingMiddleware : ICommandMiddleware
        {
            private readonly string _name;
            private readonly List<string> _trace;

            public OrderTrackingMiddleware(string name, List<string> trace)
            {
                _name = name;
                _trace = trace;
            }

            public async Task<TResult> InvokeAsync<TCommand, TResult>(
                TCommand command,
                Func<Task<TResult>> next,
                CancellationToken cancellationToken = default)
                where TCommand : ICommand<TResult>
            {
                _trace.Add($"{_name}:Before");
                var result = await next();
                _trace.Add($"{_name}:After");
                return result;
            }
        }

        [Fact]
        public async Task SendAsync_VoidCommand_ExecutesHandler()
        {
            var bus = new CommandBus();
            var handler = new TestVoidCommandHandler();
            bus.RegisterHandler(handler);

            await bus.SendAsync(new TestVoidCommand("Hello Monolith"));

            handler.Handled.Should().BeTrue();
            handler.ReceivedMessage.Should().Be("Hello Monolith");
        }

        [Fact]
        public async Task SendAsync_ResultCommand_ReturnsExpectedResult()
        {
            var bus = new CommandBus();
            var handler = new TestResultCommandHandler();
            bus.RegisterHandler(handler);

            var result = await bus.SendAsync<TestResultCommand, int>(new TestResultCommand(5, 7));

            result.Should().Be(12);
        }

        [Fact]
        public async Task SendAsync_UnregisteredCommand_ThrowsInvalidOperationException()
        {
            var bus = new CommandBus();

            Func<Task> act = async () => await bus.SendAsync(new TestVoidCommand("test"));

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*No command handler registered*");
        }

        [Fact]
        public async Task SendAsync_ExecutesMiddlewaresInRegistrationOrder()
        {
            var bus = new CommandBus();
            var handler = new TestResultCommandHandler();
            bus.RegisterHandler(handler);

            var trace = new List<string>();
            bus.AddMiddleware(new OrderTrackingMiddleware("M1", trace));
            bus.AddMiddleware(new OrderTrackingMiddleware("M2", trace));

            var result = await bus.SendAsync<TestResultCommand, int>(new TestResultCommand(10, 20));

            result.Should().Be(30);
            trace.Should().ContainInOrder("M1:Before", "M2:Before", "M2:After", "M1:After");
        }

        [Fact]
        public async Task SendAsync_ResolvesHandlerFromServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddTransient<ICommandHandler<TestResultCommand, int>, TestResultCommandHandler>();

            var sp = services.BuildServiceProvider();
            var bus = new CommandBus(sp);

            var result = await bus.SendAsync<TestResultCommand, int>(new TestResultCommand(20, 22));

            result.Should().Be(42);
        }

        [Fact]
        public async Task SendAsync_VoidCommand_ResolvesFromServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddSingleton<TestVoidCommandHandler>();
            services.AddTransient<ICommandHandler<TestVoidCommand>>(sp => sp.GetRequiredService<TestVoidCommandHandler>());

            var sp = services.BuildServiceProvider();
            var bus = new CommandBus(sp);

            await bus.SendAsync(new TestVoidCommand("Via DI"));

            var handler = sp.GetRequiredService<TestVoidCommandHandler>();
            handler.Handled.Should().BeTrue();
            handler.ReceivedMessage.Should().Be("Via DI");
        }

        [Fact]
        public async Task SendAsync_PropagatesCancellationToken()
        {
            var bus = new CommandBus();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var handler = new TestVoidCommandHandler();
            bus.RegisterHandler(handler);

            Func<Task> act = async () => await bus.SendAsync(new TestVoidCommand("Cancel"), cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
