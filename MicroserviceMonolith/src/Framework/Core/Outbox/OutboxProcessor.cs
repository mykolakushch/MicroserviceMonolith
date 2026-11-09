using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Context;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Outbox
{
    /// <summary>
    /// Contract for outbox message background processing.
    /// </summary>
    public interface IOutboxProcessor
    {
        /// <summary>
        /// Processes a single batch of pending outbox messages and dispatches them via <see cref="IEventPublisher"/>.
        /// </summary>
        /// <param name="batchSize">Number of messages to process.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The number of messages successfully processed.</returns>
        Task<int> ProcessBatchAsync(int batchSize = 50, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Default processor for dispatching transactional outbox messages.
    /// Uses delegate caching to eliminate runtime reflection lookups in the processing loop
    /// and restores ambient <see cref="CorrelationContext"/> for end-to-end tracing.
    /// </summary>
    public class OutboxProcessor : IOutboxProcessor
    {
        private readonly IOutboxStore _outboxStore;
        private readonly IEventPublisher _eventPublisher;
        private readonly ICorrelationContextAccessor? _correlationAccessor;
        private readonly Action<string>? _logAction;

        private static readonly ConcurrentDictionary<Type, Func<IEventPublisher, object, CancellationToken, Task>> _publishDelegates = new();

        public OutboxProcessor(IOutboxStore outboxStore, IEventPublisher eventPublisher, ILogger? logger = null)
            : this(outboxStore, eventPublisher, null, logger != null ? logger.Log : null)
        {
        }

        public OutboxProcessor(
            IOutboxStore outboxStore,
            IEventPublisher eventPublisher,
            ICorrelationContextAccessor? correlationAccessor,
            Action<string>? logAction = null)
        {
            _outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
            _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
            _correlationAccessor = correlationAccessor;
            _logAction = logAction;
        }

        /// <inheritdoc />
        public async Task<int> ProcessBatchAsync(int batchSize = 50, CancellationToken cancellationToken = default)
        {
            var pendingMessages = await _outboxStore.GetPendingMessagesAsync(batchSize, cancellationToken);
            int processedCount = 0;

            foreach (var message in pendingMessages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Restore correlation context if available
                    if (_correlationAccessor != null && !string.IsNullOrWhiteSpace(message.CorrelationId))
                    {
                        _correlationAccessor.CorrelationContext = new CorrelationContext(
                            message.CorrelationId,
                            causationId: message.CausationId ?? message.Id.ToString());
                    }

                    var eventObj = message.Event;
                    var eventType = eventObj.GetType();

                    // Retrieve cached dispatch delegate (zero reflection in hot path)
                    var publishDelegate = _publishDelegates.GetOrAdd(eventType, t =>
                    {
                        var method = typeof(IEventPublisher)
                            .GetMethods()
                            .First(m => m.Name == nameof(IEventPublisher.PublishAsync) && m.GetParameters().Length == 2)
                            .MakeGenericMethod(t);

                        return (pub, e, ct) => (Task)method.Invoke(pub, new object[] { e, ct })!;
                    });

                    await publishDelegate(_eventPublisher, eventObj, cancellationToken);

                    await _outboxStore.MarkAsProcessedAsync(message.Id, cancellationToken);
                    processedCount++;
                    _logAction?.Invoke($"[Outbox] Processed message {message.Id} of type {message.EventType}");
                }
                catch (Exception ex)
                {
                    _logAction?.Invoke($"[Outbox] Failed to process message {message.Id}: {ex.Message}");
                    await _outboxStore.MarkAsFailedAsync(message.Id, ex.Message, cancellationToken);
                }
            }

            return processedCount;
        }
    }
}
