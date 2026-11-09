using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using MicroserviceMonolith.Core.Logging;

namespace MicroserviceMonolith.Core.Outbox
{
    /// <summary>
    /// Settings of the background outbox processing loop.
    /// </summary>
    public sealed class OutboxProcessingOptions
    {
        /// <summary>
        /// Pause between polls of the outbox. Default: 1 second.
        /// </summary>
        public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Maximum number of messages handled per batch. Default: 50.
        /// </summary>
        public int BatchSize { get; set; } = 50;
    }

    /// <summary>
    /// Background service that keeps calling <see cref="IOutboxProcessor.ProcessBatchAsync"/> while the host runs.
    /// Registered by <c>AddMicroserviceMonolith</c> unless <c>MicroserviceMonolithOptions.AutoProcessOutbox</c> is false.
    /// </summary>
    public sealed class OutboxProcessorHostedService : BackgroundService
    {
        private readonly IOutboxProcessor _processor;
        private readonly OutboxProcessingOptions _options;
        private readonly ILogger? _logger;

        public OutboxProcessorHostedService(IOutboxProcessor processor, OutboxProcessingOptions options, ILogger? logger = null)
        {
            _processor = processor ?? throw new ArgumentNullException(nameof(processor));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;

            if (_options.PollingInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "PollingInterval must be greater than zero.");
            }

            if (_options.BatchSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "BatchSize must be at least 1.");
            }
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(_options.PollingInterval);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await DrainAsync(stoppingToken);

                    if (!await timer.WaitForNextTickAsync(stoppingToken))
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host is stopping
            }
        }

        /// <summary>
        /// Processes batches until a batch is not full, so a backlog is worked off without waiting for the next tick.
        /// A failing store or processor is logged and retried on the next tick; it never stops the service.
        /// </summary>
        private async Task DrainAsync(CancellationToken cancellationToken)
        {
            try
            {
                int processed;
                do
                {
                    processed = await _processor.ProcessBatchAsync(_options.BatchSize, cancellationToken);
                }
                while (processed >= _options.BatchSize && !cancellationToken.IsCancellationRequested);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                _logger?.Log($"[Outbox] Background processing failed: {ex}");
            }
        }
    }
}
