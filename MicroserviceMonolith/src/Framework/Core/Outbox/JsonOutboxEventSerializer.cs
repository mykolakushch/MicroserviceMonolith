using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;

namespace MicroserviceMonolith.Core.Outbox
{
    /// <summary>
    /// System.Text.Json based <see cref="IOutboxEventSerializer"/> with an explicit registry of event types.
    /// Every event type is registered under a stable name and a current schema version; older stored versions can
    /// be upgraded with <see cref="RegisterUpcaster"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddSingleton&lt;IOutboxEventSerializer&gt;(new JsonOutboxEventSerializer()
    ///     .Register&lt;OrderPlaced&gt;("orders.order-placed", schemaVersion: 2)
    ///     .RegisterUpcaster("orders.order-placed", fromVersion: 1, json =&gt; json.Replace("\"Name\"", "\"Title\"")));
    /// </code>
    /// </example>
    public sealed class JsonOutboxEventSerializer : IOutboxEventSerializer
    {
        private sealed record Registration(string Name, Type Type, int Version);

        private readonly ConcurrentDictionary<string, Registration> _byName = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<Type, Registration> _byType = new();
        private readonly ConcurrentDictionary<(string Name, int FromVersion), Func<string, string>> _upcasters = new();
        private readonly JsonSerializerOptions _options;

        public JsonOutboxEventSerializer(JsonSerializerOptions? options = null)
        {
            _options = options ?? new JsonSerializerOptions();
        }

        /// <summary>
        /// Registers an event type under a stable name. The name defaults to the CLR type name; choose an explicit
        /// name for events that are stored for a long time.
        /// </summary>
        public JsonOutboxEventSerializer Register<TEvent>(string? eventType = null, int schemaVersion = 1) where TEvent : class
        {
            if (schemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            var name = string.IsNullOrWhiteSpace(eventType) ? typeof(TEvent).Name : eventType!;
            var registration = new Registration(name, typeof(TEvent), schemaVersion);

            if (!_byName.TryAdd(name, registration) && _byName[name].Type != typeof(TEvent))
            {
                throw new InvalidOperationException($"Event type name '{name}' is already registered for '{_byName[name].Type.FullName}'.");
            }

            _byName[name] = registration;
            _byType[typeof(TEvent)] = registration;
            return this;
        }

        /// <summary>
        /// Registers a function that upgrades the JSON payload of <paramref name="eventType"/> from
        /// <paramref name="fromVersion"/> to <c>fromVersion + 1</c>. Upgrades are chained until the current version.
        /// </summary>
        public JsonOutboxEventSerializer RegisterUpcaster(string eventType, int fromVersion, Func<string, string> upgrade)
        {
            if (string.IsNullOrWhiteSpace(eventType)) throw new ArgumentException("Event type is required.", nameof(eventType));
            if (fromVersion < 1) throw new ArgumentOutOfRangeException(nameof(fromVersion));
            _upcasters[(eventType, fromVersion)] = upgrade ?? throw new ArgumentNullException(nameof(upgrade));
            return this;
        }

        /// <inheritdoc />
        public OutboxPayload Serialize(object @event)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));

            if (!_byType.TryGetValue(@event.GetType(), out var registration))
            {
                throw new InvalidOperationException(
                    $"Event type '{@event.GetType().FullName}' is not registered in the outbox serializer. Call Register<{@event.GetType().Name}>().");
            }

            return new OutboxPayload(
                registration.Name,
                registration.Version,
                JsonSerializer.Serialize(@event, registration.Type, _options));
        }

        /// <inheritdoc />
        public object Deserialize(string eventType, int schemaVersion, string payload)
        {
            if (!_byName.TryGetValue(eventType, out var registration))
            {
                throw new InvalidOperationException($"Unknown outbox event type '{eventType}'.");
            }

            if (schemaVersion > registration.Version)
            {
                throw new InvalidOperationException(
                    $"Outbox event '{eventType}' has schema version {schemaVersion}, newer than the supported version {registration.Version}.");
            }

            var version = schemaVersion;
            while (version < registration.Version)
            {
                if (!_upcasters.TryGetValue((eventType, version), out var upgrade))
                {
                    throw new InvalidOperationException(
                        $"No upcaster registered to upgrade outbox event '{eventType}' from schema version {version} to {version + 1}.");
                }

                payload = upgrade(payload);
                version++;
            }

            return JsonSerializer.Deserialize(payload, registration.Type, _options)
                   ?? throw new InvalidOperationException($"Outbox event '{eventType}' has an empty payload.");
        }
    }
}
