using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Commands;
using MicroserviceMonolith.Core.DependencyInjection;
using MicroserviceMonolith.Core.Events;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class EventFailureIsolationTests
    {
        public record IsolationEvent : EventBase;

        public record OpenGenericCommand : ICommand;

        /// <summary>Open generic handler: must be ignored by assembly scanning instead of breaking registration.</summary>
        public class OpenGenericHandler<T> : ICommandHandler<OpenGenericCommand>
        {
            public Task HandleAsync(OpenGenericCommand command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        [Theory]
        [InlineData(EventPublishStrategy.Sequential)]
        [InlineData(EventPublishStrategy.Parallel)]
        public async Task HandlerTimeout_SurfacingAsCancellation_DoesNotStopOtherHandlers(EventPublishStrategy strategy)
        {
            var bus = new EventBus();
            var ran = new ConcurrentBag<string>();
            bus.Subscribe<IsolationEvent>(_ => throw new TaskCanceledException("internal timeout"));
            bus.Subscribe<IsolationEvent>(_ => { ran.Add("second"); return Task.CompletedTask; });

            Func<Task> act = () => bus.PublishAsync(new IsolationEvent(), strategy);

            await act.Should().ThrowAsync<TaskCanceledException>().WithMessage("internal timeout");
            ran.Should().Contain("second");
        }

        [Theory]
        [InlineData(EventPublishStrategy.Sequential)]
        [InlineData(EventPublishStrategy.Parallel)]
        public async Task CallerCancellation_StillAbortsThePublication(EventPublishStrategy strategy)
        {
            var bus = new EventBus();
            using var cts = new CancellationTokenSource();
            bus.Subscribe<IsolationEvent>((_, ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

            Func<Task> act = () => bus.PublishAsync(new IsolationEvent(), strategy, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task SingleFailure_KeepsItsOriginalStackTrace()
        {
            var bus = new EventBus();
            bus.Subscribe<IsolationEvent>(async _ =>
            {
                await Task.Yield();
                ThrowFromNamedMethod();
            });

            Func<Task> act = () => bus.PublishAsync(new IsolationEvent());

            var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
            thrown.StackTrace.Should().Contain(nameof(ThrowFromNamedMethod));
        }

        private static void ThrowFromNamedMethod() => throw new InvalidOperationException("origin");

        [Fact]
        public void Scanning_IgnoresOpenGenericHandlers()
        {
            var services = new ServiceCollection();

            Action act = () => services.AddMicroserviceMonolith(o => o.RegisterServicesFromAssemblies(typeof(EventFailureIsolationTests).Assembly));

            act.Should().NotThrow();
            services.Any(d => d.ImplementationType == typeof(OpenGenericHandler<>)).Should().BeFalse();
        }
    }
}
