using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Commands;
using MicroserviceMonolith.Core.Common;
using MicroserviceMonolith.Core.Context;
using MicroserviceMonolith.Core.DependencyInjection;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Core.Execution;
using MicroserviceMonolith.Core.Outbox;
using MicroserviceMonolith.Core.Queries;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class ProductionHardeningTests
    {
        // =========================================================================
        // 1. Internal Handler Test (Encapsulation)
        // =========================================================================
        public record InternalTestCommand(string Data) : ICommand;

        internal class InternalTestCommandHandler : ICommandHandler<InternalTestCommand>
        {
            public static bool Handled { get; set; }

            public Task HandleAsync(InternalTestCommand command, CancellationToken cancellationToken = default)
            {
                Handled = true;
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task AssemblyScanner_DiscoversAndExecutesInternalHandlers()
        {
            var services = new ServiceCollection();
            services.AddMicroserviceMonolith(options =>
            {
                options.RegisterServicesFromAssemblies(typeof(ProductionHardeningTests).Assembly);
            });

            var sp = services.BuildServiceProvider();
            var commandBus = sp.GetRequiredService<ICommandBus>();

            InternalTestCommandHandler.Handled = false;
            await commandBus.SendAsync(new InternalTestCommand("Secret"));

            InternalTestCommandHandler.Handled.Should().BeTrue();
        }

        // =========================================================================
        // 2. EventBus Idempotency Test with Per-Handler Retry
        // =========================================================================
        public record MultiSubscriberEvent(string ItemId) : EventBase;

        [Fact]
        public async Task EventBus_PerHandlerRetry_PreservesIdempotencyOfSuccessfulSubscribers()
        {
            var bus = new EventBus();
            int sub1Calls = 0;
            int sub2Calls = 0;
            int sub3Calls = 0;

            // Subscriber 1: Always succeeds
            bus.Subscribe<MultiSubscriberEvent>(_ =>
            {
                Interlocked.Increment(ref sub1Calls);
                return Task.CompletedTask;
            });

            // Subscriber 2: Always succeeds
            bus.Subscribe<MultiSubscriberEvent>(_ =>
            {
                Interlocked.Increment(ref sub2Calls);
                return Task.CompletedTask;
            });

            // Subscriber 3: Fails on first 2 attempts, succeeds on 3rd
            bus.Subscribe<MultiSubscriberEvent>(_ =>
            {
                var attempt = Interlocked.Increment(ref sub3Calls);
                if (attempt < 3)
                {
                    throw new InvalidOperationException($"Sub3 attempt {attempt} failed");
                }
                return Task.CompletedTask;
            });

            // Register retry middleware as IEventHandlerMiddleware
            bus.AddHandlerMiddleware(new RetryEventMiddleware(3, TimeSpan.FromMilliseconds(5), 1.0, false));

            await bus.PublishAsync(new MultiSubscriberEvent("Item-123"));

            // Subscriber 1 and 2 must have been called EXACTLY ONCE (not retried!)
            sub1Calls.Should().Be(1);
            sub2Calls.Should().Be(1);
            // Subscriber 3 retried until success
            sub3Calls.Should().Be(3);
        }

        // =========================================================================
        // 3. CorrelationContext End-to-End Propagation
        // =========================================================================
        public record CorrelatedCommand : ICommand;

        public class CorrelatedCommandHandler : ICommandHandler<CorrelatedCommand>
        {
            private readonly ICorrelationContextAccessor _accessor;
            public string? CapturedCorrelationId { get; private set; }

            public CorrelatedCommandHandler(ICorrelationContextAccessor accessor)
            {
                _accessor = accessor;
            }

            public Task HandleAsync(CorrelatedCommand command, CancellationToken cancellationToken = default)
            {
                CapturedCorrelationId = _accessor.CorrelationContext?.CorrelationId;
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task CommandBus_AutomaticallyInitializesAndPropagatesCorrelationContext()
        {
            var services = new ServiceCollection();
            services.AddSingleton<CorrelatedCommandHandler>();
            services.AddTransient<ICommandHandler<CorrelatedCommand>>(sp => sp.GetRequiredService<CorrelatedCommandHandler>());
            services.AddMicroserviceMonolith();

            var sp = services.BuildServiceProvider();
            var bus = sp.GetRequiredService<ICommandBus>();
            var accessor = sp.GetRequiredService<ICorrelationContextAccessor>();

            accessor.CorrelationContext.Should().BeNull();

            // When no context is set, CommandBus initializes one that flows into the handler
            await bus.SendAsync(new CorrelatedCommand());

            var handler = sp.GetRequiredService<CorrelatedCommandHandler>();
            handler.CapturedCorrelationId.Should().NotBeNullOrEmpty();

            // When caller sets a context, it flows down into the handler
            accessor.CorrelationContext = new CorrelationContext("CALLER-ID-777");
            await bus.SendAsync(new CorrelatedCommand());

            handler.CapturedCorrelationId.Should().Be("CALLER-ID-777");
        }

        [Fact]
        public async Task ExecutionQueue_PropagatesCorrelationContextToBackgroundWorker()
        {
            var accessor = new CorrelationContextAccessor();
            accessor.CorrelationContext = new CorrelationContext("CUSTOM-CID-999");

            await using var queue = new ExecutionQueue(new ExecutionQueueOptions(), accessor);

            string? workerCorrelationId = null;
            var tcs = new TaskCompletionSource<bool>();

            queue.Enqueue(async () =>
            {
                workerCorrelationId = accessor.CorrelationContext?.CorrelationId;
                tcs.TrySetResult(true);
                await Task.CompletedTask;
            });

            await Task.WhenAny(tcs.Task, Task.Delay(2000));
            workerCorrelationId.Should().Be("CUSTOM-CID-999");
        }

        // =========================================================================
        // 4. Outbox Poison Message and Memory Leak Prevention
        // =========================================================================
        [Fact]
        public async Task InMemoryOutboxStore_PreventsPoisonMessageInfiniteLoop_AndCleansUpProcessed()
        {
            var store = new InMemoryOutboxStore { MaxRetries = 3 };

            var poisonMessage = new OutboxMessage
            {
                EventType = "FailingEvent",
                Event = new MultiSubscriberEvent("fail-item")
            };
            await store.SaveAsync(poisonMessage);

            // Simulate 3 failures
            await store.MarkAsFailedAsync(poisonMessage.Id, "Fail 1");
            await store.MarkAsFailedAsync(poisonMessage.Id, "Fail 2");
            await store.MarkAsFailedAsync(poisonMessage.Id, "Fail 3");

            // Exceeds MaxRetries => must no longer be returned as pending!
            var pending = await store.GetPendingMessagesAsync();
            pending.Should().BeEmpty();

            // Save and process a valid message
            var validMessage = new OutboxMessage
            {
                EventType = "ValidEvent",
                Event = new MultiSubscriberEvent("valid-item")
            };
            await store.SaveAsync(validMessage);

            var pendingValid = await store.GetPendingMessagesAsync();
            pendingValid.Should().ContainSingle();

            // Mark as processed => must be purged from memory to prevent memory leak
            await store.MarkAsProcessedAsync(validMessage.Id);
            var pendingAfter = await store.GetPendingMessagesAsync();
            pendingAfter.Should().BeEmpty();
        }

        // =========================================================================
        // 5. Zero-Reflection Void Command Resolution
        // =========================================================================
        [Fact]
        public void ICommandHandler_ImplementsUnitInterfaceByDefault()
        {
            var handler = new InternalTestCommandHandler();
            var unitHandler = handler as ICommandHandler<InternalTestCommand, Unit>;

            unitHandler.Should().NotBeNull();
        }

        // =========================================================================
        // 6. Factory Registrations Not Duplicated by Assembly Scanner
        // =========================================================================
        public record FactoryTestCommand(string Name) : ICommand;

        public class FactoryTestCommandHandler : ICommandHandler<FactoryTestCommand>
        {
            public Task HandleAsync(FactoryTestCommand command, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }
        }

        [Fact]
        public void FactoryRegisteredHandler_NotDuplicatedByAssemblyScanner()
        {
            var services = new ServiceCollection();

            // Module registers handler via factory (ImplementationType == null)
            services.AddTransient<ICommandHandler<FactoryTestCommand>>(sp => new FactoryTestCommandHandler());

            services.AddMicroserviceMonolith(options =>
            {
                options.RegisterServicesFromAssemblies(typeof(ProductionHardeningTests).Assembly);
            });

            // Must have exactly 1 registration for FactoryTestCommand
            var handlers = services.Where(s => s.ServiceType == typeof(ICommandHandler<FactoryTestCommand>)).ToList();
            handlers.Should().ContainSingle();
        }
    }
}
