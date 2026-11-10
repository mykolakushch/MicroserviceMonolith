namespace MicroserviceMonolith.Core.Outbox
{
    /// <summary>
    /// The durable, transport-neutral form of an event: a stable type name, a schema version and a serialized payload.
    /// </summary>
    public readonly record struct OutboxPayload(string EventType, int SchemaVersion, string Payload);

    /// <summary>
    /// Converts events to and from the form that is stored in a durable outbox. Storing the stable
    /// <see cref="OutboxPayload.EventType"/> name and a schema version (instead of a CLR type) lets events survive
    /// type renames, assembly changes and schema evolution.
    /// </summary>
    public interface IOutboxEventSerializer
    {
        /// <summary>
        /// Serializes an event.
        /// </summary>
        OutboxPayload Serialize(object @event);

        /// <summary>
        /// Recreates an event from its stored form.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">The event type is unknown or its schema cannot be read.</exception>
        object Deserialize(string eventType, int schemaVersion, string payload);
    }
}
