using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MicroserviceMonolith.Core.Events;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class EventBusParallelTests
    {
        public record ParallelEvent(string Id) : EventBase;

        [Fact]
        public async Task Parallel_SynchronousThrow_DoesNotPreventOtherHandlersFromRunning()
        {
            var bus = new EventBus();
            var ran = new ConcurrentBag<string>();

            bus.Subscribe<ParallelEvent>(_ => throw new InvalidOperationException("sync failure"));
            bus.Subscribe<ParallelEvent>(_ => { ran.Add("second"); return Task.CompletedTask; });
            bus.Subscribe<ParallelEvent>(async _ => { await Task.Yield(); ran.Add("third"); });

            Func<Task> act = () => bus.PublishAsync(new ParallelEvent("1"), EventPublishStrategy.Parallel);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("sync failure");
            ran.Should().BeEquivalentTo(new[] { "second", "third" });
        }

        [Fact]
        public async Task Parallel_MultipleFailures_AreAggregated()
        {
            var bus = new EventBus();
            bus.Subscribe<ParallelEvent>(_ => throw new InvalidOperationException("first"));
            bus.Subscribe<ParallelEvent>(async _ =>
            {
                await Task.Yield();
                throw new ArgumentException("second");
            });

            Func<Task> act = () => bus.PublishAsync(new ParallelEvent("1"), EventPublishStrategy.Parallel);

            var thrown = (await act.Should().ThrowAsync<AggregateException>()).Which;
            thrown.InnerExceptions.Select(e => e.Message).Should().BeEquivalentTo(new[] { "first", "second" });
        }

        [Fact]
        public async Task Parallel_SingleFailure_IsRethrownUnwrapped()
        {
            var bus = new EventBus();
            bus.Subscribe<ParallelEvent>(async _ =>
            {
                await Task.Yield();
                throw new TimeoutException("slow");
            });
            bus.Subscribe<ParallelEvent>(_ => Task.CompletedTask);

            Func<Task> act = () => bus.PublishAsync(new ParallelEvent("1"), EventPublishStrategy.Parallel);

            await act.Should().ThrowAsync<TimeoutException>();
        }

        [Fact]
        public async Task Parallel_AllHandlersSucceed_Completes()
        {
            var bus = new EventBus();
            var count = 0;
            for (var i = 0; i < 5; i++)
            {
                bus.Subscribe<ParallelEvent>(async _ =>
                {
                    await Task.Yield();
                    Interlocked.Increment(ref count);
                });
            }

            await bus.PublishAsync(new ParallelEvent("1"), EventPublishStrategy.Parallel);

            count.Should().Be(5);
        }

        [Fact]
        public async Task Parallel_CancelledHandler_SurfacesAsOperationCanceled()
        {
            var bus = new EventBus();
            using var cts = new CancellationTokenSource();
            bus.Subscribe<ParallelEvent>(async (_, ct) =>
            {
                cts.Cancel();
                await Task.Delay(Timeout.Infinite, ct);
            });

            Func<Task> act = () => bus.PublishAsync(new ParallelEvent("1"), EventPublishStrategy.Parallel, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
