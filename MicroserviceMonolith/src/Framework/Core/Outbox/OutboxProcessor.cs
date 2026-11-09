using System;
using System.Threading;
using System.Threading.Tasks;
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
    /// </summary>
    public class OutboxProcessor : IOutboxProcessor
    {
        private readonly IOutboxStore _outboxStore;
        private readonly IEventPublisher _eventPublisher;
        private readonly Action<string>? _logAction;

        public OutboxProcessor(IOutboxStore outboxStore, IEventPublisher eventPublisher, ILogger? logger = null)
        {
            _outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
            _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
            _logAction = logger != null ? logger.Log : null;
        }

        public OutboxProcessor(IOutboxStore outboxStore, IEventPublisher eventPublisher, Action<string>? logAction)
        {
            _outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
            _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
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
                    var eventObj = message.Event;
                    var publishMethod = typeof(IEventPublisher)
                        .GetMethods()
                        .First(m => m.Name == nameof(IEventPublisher.PublishAsync) && m.GetParameters().Length == 2)
                        .MakeGenericMethod(eventObj.GetType());

                    var task = (Task)publishMethod.Invoke(_eventPublisher, new object[] { eventObj, cancellationToken })!;
                    await task;

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
