using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MicroserviceMonolith.Core.Commands;
using MicroserviceMonolith.Core.Queries;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    /// <summary>
    /// Covers dispatch through <see cref="ICommand{TResult}"/> / <see cref="IQuery{TResult}"/>, where the concrete
    /// message type is only known at runtime and the bus uses a per-type cached invoker.
    /// </summary>
    public class DispatchCacheTests
    {
        public record DispatchAddCommand(int A, int B) : ICommand<int>;
        public record DispatchMultiplyCommand(int A, int B) : ICommand<int>;
        public record DispatchFailingCommand : ICommand<int>;
        public record DispatchDoubleQuery(int Value) : IQuery<int>;
        public record DispatchNegateQuery(int Value) : IQuery<int>;
        public record DispatchFailingQuery : IQuery<int>;

        private sealed class DispatchAddHandler : ICommandHandler<DispatchAddCommand, int>
        {
            public Task<int> HandleAsync(DispatchAddCommand command, CancellationToken cancellationToken = default)
                => Task.FromResult(command.A + command.B);
        }

        private sealed class DispatchMultiplyHandler : ICommandHandler<DispatchMultiplyCommand, int>
        {
            public Task<int> HandleAsync(DispatchMultiplyCommand command, CancellationToken cancellationToken = default)
                => Task.FromResult(command.A * command.B);
        }

        private sealed class DispatchFailingCommandHandler : ICommandHandler<DispatchFailingCommand, int>
        {
            public Task<int> HandleAsync(DispatchFailingCommand command, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("command boom");
        }

        private sealed class DispatchDoubleHandler : IQueryHandler<DispatchDoubleQuery, int>
        {
            public Task<int> HandleAsync(DispatchDoubleQuery query, CancellationToken cancellationToken = default)
                => Task.FromResult(query.Value * 2);
        }

        private sealed class DispatchNegateHandler : IQueryHandler<DispatchNegateQuery, int>
        {
            public Task<int> HandleAsync(DispatchNegateQuery query, CancellationToken cancellationToken = default)
                => Task.FromResult(-query.Value);
        }

        private sealed class DispatchFailingQueryHandler : IQueryHandler<DispatchFailingQuery, int>
        {
            public Task<int> HandleAsync(DispatchFailingQuery query, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("query boom");
        }

        [Fact]
        public async Task SendAsync_ThroughResultInterface_RoutesEachCommandTypeToItsOwnHandler()
        {
            var bus = new CommandBus();
            bus.RegisterHandler<DispatchAddCommand, int>(new DispatchAddHandler());
            bus.RegisterHandler<DispatchMultiplyCommand, int>(new DispatchMultiplyHandler());

            ICommand<int> add = new DispatchAddCommand(3, 4);
            ICommand<int> multiply = new DispatchMultiplyCommand(3, 4);

            (await bus.SendAsync(add)).Should().Be(7);
            (await bus.SendAsync(multiply)).Should().Be(12);
            (await bus.SendAsync(add)).Should().Be(7); // second call uses the cached invoker
        }

        [Fact]
        public async Task SendAsync_ThroughResultInterface_PropagatesOriginalHandlerException()
        {
            var bus = new CommandBus();
            bus.RegisterHandler<DispatchFailingCommand, int>(new DispatchFailingCommandHandler());
            ICommand<int> failing = new DispatchFailingCommand();

            Func<Task> act = () => bus.SendAsync(failing);

            await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("command boom");
        }

        [Fact]
        public async Task QueryAsync_ThroughResultInterface_RoutesEachQueryTypeToItsOwnHandler()
        {
            var bus = new QueryBus();
            bus.RegisterHandler<DispatchDoubleQuery, int>(new DispatchDoubleHandler());
            bus.RegisterHandler<DispatchNegateQuery, int>(new DispatchNegateHandler());

            IQuery<int> doubled = new DispatchDoubleQuery(5);
            IQuery<int> negated = new DispatchNegateQuery(5);

            (await bus.QueryAsync(doubled)).Should().Be(10);
            (await bus.QueryAsync(negated)).Should().Be(-5);
            (await bus.QueryAsync(doubled)).Should().Be(10); // cached invoker
        }

        [Fact]
        public async Task QueryAsync_ThroughResultInterface_PropagatesOriginalHandlerException()
        {
            var bus = new QueryBus();
            bus.RegisterHandler<DispatchFailingQuery, int>(new DispatchFailingQueryHandler());
            IQuery<int> failing = new DispatchFailingQuery();

            Func<Task> act = () => bus.QueryAsync(failing);

            await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("query boom");
        }
    }
}
