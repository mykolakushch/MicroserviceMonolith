using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MicroserviceMonolith.Core.Context;
using MicroserviceMonolith.Core.DependencyInjection;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Core.Outbox;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class OutboxReliabilityTests
    {
        public record ReliabilityEvent(string Id) : EventBase;

        private sealed class ManualTimeProvider : TimeProvider
        {
            private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => _now;

            public void Advance(TimeSpan by) => _now += by;
        }

        private sealed class CancellingPublisher : IEventPublisher
        {
            private readonly CancellationTokenSource _cts;

            public CancellingPublisher(CancellationTokenSource cts) => _cts = cts;

            public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class
            {
                _cts.Cancel();
                throw new OperationCanceledException(_cts.Token);
            }

            public Task PublishAsync<TEvent>(TEvent @event, EventPublishStrategy strategy, CancellationToken cancellationToken = default) where TEvent : class
                => PublishAsync(@event, cancellationToken);
        }

        private static OutboxMessage NewMessage(string id) => new()
        {
            EventType = nameof(ReliabilityEvent),
            Event = new ReliabilityEvent(id)
        };

        [Fact]
        public async Task GetPending_LeasesMessages_SoTheyAreNotHandedOutTwice()
        {
            var time = new ManualTimeProvider();
            var store = new InMemoryOutboxStore(time) { LeaseDuration = TimeSpan.FromSeconds(30) };
            await store.SaveAsync(NewMessage("a"));
            await store.SaveAsync(NewMessage("b"));

            (await store.GetPendingMessagesAsync()).Should().HaveCount(2);
            (await store.GetPendingMessagesAsync()).Should().BeEmpty(); // leased by the first caller

            time.Advance(TimeSpan.FromSeconds(31)); // lease expired, e.g. the first processor crashed
            (await store.GetPendingMessagesAsync()).Should().HaveCount(2);
        }

        [Fact]
        public async Task MarkAsFailed_RetriesWithExponentialBackoff()
        {
            var time = new ManualTimeProvider();
            var store = new InMemoryOutboxStore(time)
            {
                MaxRetries = 10,
                RetryBaseDelay = TimeSpan.FromSeconds(1),
                RetryMaxDelay = TimeSpan.FromSeconds(3)
            };
            var message = NewMessage("a");
            await store.SaveAsync(message);

            (await store.GetPendingMessagesAsync()).Should().ContainSingle();
            await store.MarkAsFailedAsync(message.Id, "fail 1"); // retry after 1s

            (await store.GetPendingMessagesAsync()).Should().BeEmpty();
            time.Advance(TimeSpan.FromMilliseconds(999));
            (await store.GetPendingMessagesAsync()).Should().BeEmpty();
            time.Advance(TimeSpan.FromMilliseconds(1));
            (await store.GetPendingMessagesAsync()).Should().ContainSingle();

            await store.MarkAsFailedAsync(message.Id, "fail 2"); // retry after 2s
            time.Advance(TimeSpan.FromSeconds(1));
            (await store.GetPendingMessagesAsync()).Should().BeEmpty();
            time.Advance(TimeSpan.FromSeconds(1));
            (await store.GetPendingMessagesAsync()).Should().ContainSingle();

            await store.MarkAsFailedAsync(message.Id, "fail 3"); // 4s doubled, capped at 3s
            time.Advance(TimeSpan.FromMilliseconds(2900));
            (await store.GetPendingMessagesAsync()).Should().BeEmpty();
            time.Advance(TimeSpan.FromMilliseconds(100));
            (await store.GetPendingMessagesAsync()).Should().ContainSingle();
        }

        [Fact]
        public async Task DeadLetters_AreInspectableAndCanBePurged()
        {
            var store = new InMemoryOutboxStore { MaxRetries = 2, RetryBaseDelay = TimeSpan.Zero };
            var poison = NewMessage("poison");
            var healthy = NewMessage("healthy");
            await store.SaveAsync(poison);
            await store.SaveAsync(healthy);

            await store.MarkAsFailedAsync(poison.Id, "fail 1");
            store.GetDeadLetters().Should().BeEmpty(); // one retry left

            await store.MarkAsFailedAsync(poison.Id, "fail 2");
            var deadLetters = store.GetDeadLetters();
            deadLetters.Should().ContainSingle().Which.Id.Should().Be(poison.Id);
            deadLetters[0].LastError.Should().Be("fail 2");

            store.PurgeDeadLetters().Should().Be(1);
            store.GetDeadLetters().Should().BeEmpty();
            (await store.GetPendingMessagesAsync()).Should().ContainSingle().Which.Id.Should().Be(healthy.Id);
        }

        [Fact]
        public async Task Processor_CancellationByCaller_DoesNotConsumeARetry()
        {
            var store = new InMemoryOutboxStore();
            var message = NewMessage("a");
            await store.SaveAsync(message);

            using var cts = new CancellationTokenSource();
            var processor = new OutboxProcessor(store, new CancellingPublisher(cts));

            Func<Task> act = () => processor.ProcessBatchAsync(10, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            message.RetryCount.Should().Be(0);
            message.LastError.Should().BeNull();
        }

        [Fact]
        public async Task Processor_LogsDeadLetteringWhenRetriesAreExhausted()
        {
            var store = new InMemoryOutboxStore { MaxRetries = 1, RetryBaseDelay = TimeSpan.Zero };
            var bus = new EventBus();
            bus.Subscribe<ReliabilityEvent>(_ => throw new InvalidOperationException("handler failed"));
            var logs = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var processor = new OutboxProcessor(store, bus, null, logs.Enqueue);
            await store.SaveAsync(NewMessage("a"));

            var processed = await processor.ProcessBatchAsync();

            processed.Should().Be(0);
            logs.Should().Contain(l => l.Contains("dead-lettered"));
            store.GetDeadLetters().Should().ContainSingle();
        }

        [Fact]
        public async Task OutboxWriter_StampsCorrelationContext_AndProcessorDeliversFromScopedStore()
        {
            var services = new ServiceCollection();
            services.AddMicroserviceMonolith(options => options.AutoProcessOutbox = false);
            using var provider = services.BuildServiceProvider();

            var accessor = provider.GetRequiredService<ICorrelationContextAccessor>();
            string? seenCorrelationId = null;
            var received = new TaskCompletionSource<ReliabilityEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.GetRequiredService<IEventSubscriber>().Subscribe<ReliabilityEvent>(e =>
            {
                seenCorrelationId = accessor.CorrelationContext?.CorrelationId;
                received.TrySetResult(e);
                return Task.CompletedTask;
            });

            // The writer (scoped) stamps the ambient correlation context onto the stored message
            using (var scope = provider.CreateScope())
            {
                accessor.CorrelationContext = new CorrelationContext("corr-42", causationId: "cause-7");
                await scope.ServiceProvider.GetRequiredService<IOutboxWriter>().AddAsync(new ReliabilityEvent("evt-1"));
            }

            // Drop the ambient context: the processor must restore it from the stored message
            accessor.CorrelationContext = null;

            var processed = await provider.GetRequiredService<IOutboxProcessor>().ProcessBatchAsync();

            processed.Should().Be(1);
            (await received.Task.WaitAsync(TimeSpan.FromSeconds(5))).Id.Should().Be("evt-1");
            seenCorrelationId.Should().Be("corr-42");
        }

        [Fact]
        public async Task HostedService_DeliversPendingMessagesInTheBackground()
        {
            var services = new ServiceCollection();
            services.AddMicroserviceMonolith(options => options.OutboxPollingInterval = TimeSpan.FromMilliseconds(20));
            using var provider = services.BuildServiceProvider();

            var received = new TaskCompletionSource<ReliabilityEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.GetRequiredService<IEventSubscriber>().Subscribe<ReliabilityEvent>(e =>
            {
                received.TrySetResult(e);
                return Task.CompletedTask;
            });

            var hosted = provider.GetServices<IHostedService>().OfType<OutboxProcessorHostedService>().Single();
            await hosted.StartAsync(CancellationToken.None);
            try
            {
                using var scope = provider.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IOutboxWriter>().AddAsync(new ReliabilityEvent("bg-1"));

                (await received.Task.WaitAsync(TimeSpan.FromSeconds(5))).Id.Should().Be("bg-1");
            }
            finally
            {
                await hosted.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public void HostedService_IsRegisteredOnce_AndCanBeDisabled()
        {
            var enabled = new ServiceCollection();
            enabled.AddMicroserviceMonolith();
            enabled.AddMicroserviceMonolith();
            enabled.Count(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(OutboxProcessorHostedService))
                .Should().Be(1);

            var disabled = new ServiceCollection();
            disabled.AddMicroserviceMonolith(o => o.AutoProcessOutbox = false);
            disabled.Any(d => d.ImplementationType == typeof(OutboxProcessorHostedService)).Should().BeFalse();

            var noOutbox = new ServiceCollection();
            noOutbox.AddMicroserviceMonolith(o => o.EnableOutbox = false);
            noOutbox.Any(d => d.ImplementationType == typeof(OutboxProcessorHostedService)).Should().BeFalse();
        }
    }
}
