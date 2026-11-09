using System;
using System.Threading;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Context;

namespace MicroserviceMonolith.Core.Outbox
{
    /// <summary>
    /// Adds events to the outbox, stamping them with the ambient correlation context.
    /// </summary>
    /// <remarks>
    /// Resolve it in the same scope as your business data (it is registered as scoped) and use an
    /// <see cref="IOutboxStore"/> that writes through the same unit of work/transaction, so the event is stored
    /// atomically with the state change that produced it. The built-in in-memory store provides no such guarantee.
    /// </remarks>
    public interface IOutboxWriter
    {
        /// <summary>
        /// Stores the event in the outbox for later delivery by <see cref="IOutboxProcessor"/>.
        /// </summary>
        Task AddAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class;
    }

    /// <summary>
    /// Default <see cref="IOutboxWriter"/> that creates an <see cref="OutboxMessage"/> and saves it to the <see cref="IOutboxStore"/>.
    /// </summary>
    public sealed class OutboxWriter : IOutboxWriter
    {
        private readonly IOutboxStore _store;
        private readonly ICorrelationContextAccessor? _correlationAccessor;

        public OutboxWriter(IOutboxStore store, ICorrelationContextAccessor? correlationAccessor = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _correlationAccessor = correlationAccessor;
        }

        /// <inheritdoc />
        public Task AddAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));

            var context = _correlationAccessor?.CorrelationContext;
            var message = new OutboxMessage
            {
                EventType = @event.GetType().Name,
                Event = @event,
                CorrelationId = context?.CorrelationId,
                CausationId = context?.CausationId
            };

            return _store.SaveAsync(message, cancellationToken);
        }
    }
}
