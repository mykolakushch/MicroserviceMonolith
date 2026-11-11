using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.DependencyInjection;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Core.Outbox;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class OutboxPayloadTests
    {
        public record PayloadEvent(string Id, int Amount);

        public record OrderV2(string Title);

        [Fact]
        public void Serializer_RoundTrips_UnderAStableName()
        {
            var serializer = new JsonOutboxEventSerializer().Register<PayloadEvent>("payments.payload-event");

            var stored = serializer.Serialize(new PayloadEvent("p-1", 42));
            var restored = (PayloadEvent)serializer.Deserialize(stored.EventType, stored.SchemaVersion, stored.Payload);

            stored.EventType.Should().Be("payments.payload-event"); // not the CLR type name
            stored.SchemaVersion.Should().Be(1);
            restored.Id.Should().Be("p-1");
            restored.Amount.Should().Be(42);
        }

        [Fact]
        public void Serializer_RejectsUnknownTypes_AndNewerSchemas()
        {
            var serializer = new JsonOutboxEventSerializer().Register<PayloadEvent>("p", schemaVersion: 2)
                .RegisterUpcaster("p", 1, json => json);

            Action unregistered = () => serializer.Serialize(new OrderV2("x"));
            Action unknown = () => serializer.Deserialize("missing", 1, "{}");
            Action newer = () => serializer.Deserialize("p", 3, "{}");

            unregistered.Should().Throw<InvalidOperationException>().WithMessage("*not registered*");
            unknown.Should().Throw<InvalidOperationException>().WithMessage("*Unknown*missing*");
            newer.Should().Throw<InvalidOperationException>().WithMessage("*newer*");
        }

        [Fact]
        public void Serializer_UpgradesOlderSchemaVersions_WithUpcasters()
        {
            var serializer = new JsonOutboxEventSerializer()
                .Register<OrderV2>("orders.order", schemaVersion: 2)
                .RegisterUpcaster("orders.order", fromVersion: 1, json => json.Replace("\"Name\"", "\"Title\""));

            var restored = (OrderV2)serializer.Deserialize("orders.order", 1, "{\"Name\":\"Book\"}");

            restored.Title.Should().Be("Book");
        }

        [Fact]
        public void Serializer_WithoutAnUpcaster_FailsForOlderVersions()
        {
            var serializer = new JsonOutboxEventSerializer().Register<OrderV2>("orders.order", schemaVersion: 2);

            Action act = () => serializer.Deserialize("orders.order", 1, "{}");

            act.Should().Throw<InvalidOperationException>().WithMessage("*No upcaster*");
        }

        [Fact]
        public async Task Processor_RecreatesTheEventFromItsPayload_WhenTheMessageHasNoInMemoryEvent()
        {
            var serializer = new JsonOutboxEventSerializer().Register<PayloadEvent>("payments.payload-event");
            var store = new InMemoryOutboxStore();
            var bus = new EventBus();
            PayloadEvent? received = null;
            bus.Subscribe<PayloadEvent>(e =>
            {
                received = e;
                return Task.CompletedTask;
            });
            var processor = new OutboxProcessor(store, bus, null, (Action<string>?)null) { EventSerializer = serializer };

            // What a durable store hands back after a restart: no CLR object, only the stored form
            await store.SaveAsync(new OutboxMessage
            {
                EventType = "payments.payload-event",
                SchemaVersion = 1,
                Payload = "{\"Id\":\"p-9\",\"Amount\":7}"
            });

            var processed = await processor.ProcessBatchAsync();

            processed.Should().Be(1);
            received.Should().NotBeNull();
            received!.Id.Should().Be("p-9");
            received.Amount.Should().Be(7);
        }

        [Fact]
        public async Task Processor_FailsTheMessage_WhenItCannotBeRecreated()
        {
            var store = new InMemoryOutboxStore { RetryBaseDelay = TimeSpan.Zero };
            var processor = new OutboxProcessor(store, new EventBus(), null, (Action<string>?)null);
            var message = new OutboxMessage { EventType = "payments.payload-event", Payload = "{}" };
            await store.SaveAsync(message);

            var processed = await processor.ProcessBatchAsync();

            processed.Should().Be(0);
            message.RetryCount.Should().Be(1);
            message.LastError.Should().Contain("no IOutboxEventSerializer");
        }

        [Fact]
        public async Task Writer_StoresStableTypeVersionAndPayload_WhenASerializerIsRegistered()
        {
            var services = new ServiceCollection();
            services.AddMicroserviceMonolith(o => { o.EnableOutbox = true; o.AutoProcessOutbox = false; });
            services.AddSingleton<IOutboxEventSerializer>(
                new JsonOutboxEventSerializer().Register<PayloadEvent>("payments.payload-event", schemaVersion: 3));
            using var provider = services.BuildServiceProvider();

            using (var scope = provider.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IOutboxWriter>().AddAsync(new PayloadEvent("p-1", 5));
            }

            var message = (await provider.GetRequiredService<IOutboxStore>().GetPendingMessagesAsync()).Single();
            message.EventType.Should().Be("payments.payload-event");
            message.SchemaVersion.Should().Be(3);
            message.Payload.Should().Contain("\"p-1\"");
        }

        [Fact]
        public async Task Writer_WithoutASerializer_KeepsTheClrTypeNameAndNoPayload()
        {
            var services = new ServiceCollection();
            services.AddMicroserviceMonolith(o => { o.EnableOutbox = true; o.AutoProcessOutbox = false; });
            using var provider = services.BuildServiceProvider();

            using (var scope = provider.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IOutboxWriter>().AddAsync(new PayloadEvent("p-1", 5));
            }

            var message = (await provider.GetRequiredService<IOutboxStore>().GetPendingMessagesAsync()).Single();
            message.EventType.Should().Be(nameof(PayloadEvent));
            message.Payload.Should().BeNull();
            message.Event.Should().BeOfType<PayloadEvent>();
        }
    }
}
