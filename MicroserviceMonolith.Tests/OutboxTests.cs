using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Core.Outbox;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class OutboxTests
    {
        public record PaymentCompletedEvent(string PaymentId, decimal Amount) : EventBase;

        [Fact]
        public async Task OutboxStore_SavesAndRetrievesPendingMessages()
        {
            var store = new InMemoryOutboxStore();
            var msg = new OutboxMessage
            {
                EventType = nameof(PaymentCompletedEvent),
                Event = new PaymentCompletedEvent("PAY-1", 150m)
            };

            await store.SaveAsync(msg);
            var pending = await store.GetPendingMessagesAsync(10);

            pending.Should().HaveCount(1);
            pending[0].Id.Should().Be(msg.Id);

            await store.MarkAsProcessedAsync(msg.Id);
            var pendingAfter = await store.GetPendingMessagesAsync(10);
            pendingAfter.Should().BeEmpty();
        }

        [Fact]
        public async Task OutboxProcessor_DispatchesBatchAndMarksProcessed()
        {
            var store = new InMemoryOutboxStore();
            var eventBus = new EventBus();
            var processor = new OutboxProcessor(store, eventBus);

            PaymentCompletedEvent? received = null;
            eventBus.Subscribe<PaymentCompletedEvent>(evt =>
            {
                received = evt;
                return Task.CompletedTask;
            });

            var msg = new OutboxMessage
            {
                EventType = nameof(PaymentCompletedEvent),
                Event = new PaymentCompletedEvent("PAY-2", 200m)
            };
            await store.SaveAsync(msg);

            var processedCount = await processor.ProcessBatchAsync();

            processedCount.Should().Be(1);
            received.Should().NotBeNull();
            received!.PaymentId.Should().Be("PAY-2");

            var pending = await store.GetPendingMessagesAsync();
            pending.Should().BeEmpty();
        }
    }
}
