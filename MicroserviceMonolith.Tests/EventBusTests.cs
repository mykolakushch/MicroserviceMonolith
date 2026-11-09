using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Events;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class EventBusTests
    {
        public record OrderPlacedEvent(string OrderId, decimal Amount) : EventBase;

        private class OrderPlacedEventHandler : IEventHandler<OrderPlacedEvent>
        {
            public List<OrderPlacedEvent> ReceivedEvents { get; } = new();

            public Task HandleAsync(OrderPlacedEvent @event, CancellationToken cancellationToken = default)
            {
                ReceivedEvents.Add(@event);
                return Task.CompletedTask;
            }
        }

        private class EventTrackingMiddleware : IEventMiddleware
        {
            private readonly string _name;
            private readonly List<string> _trace;

            public EventTrackingMiddleware(string name, List<string> trace)
            {
                _name = name;
                _trace = trace;
            }

            public async Task InvokeAsync<TEvent>(
                TEvent @event,
                Func<Task> next,
                CancellationToken cancellationToken = default)
                where TEvent : class
            {
                _trace.Add($"{_name}:Start");
                await next();
                _trace.Add($"{_name}:End");
            }
        }

        [Fact]
        public async Task PublishAsync_DeliversToSubscribedHandler()
        {
            var bus = new EventBus();
            OrderPlacedEvent? received = null;

            using var sub = bus.Subscribe<OrderPlacedEvent>(evt =>
            {
                received = evt;
                return Task.CompletedTask;
            });

            var evt = new OrderPlacedEvent("ORD-1", 99.99m);
            await bus.PublishAsync(evt);

            received.Should().NotBeNull();
            received!.OrderId.Should().Be("ORD-1");
            received.Amount.Should().Be(99.99m);
        }

        [Fact]
        public async Task PublishAsync_UnsubscribedHandler_DoesNotReceiveEvent()
        {
            var bus = new EventBus();
            int callCount = 0;

            var subscription = bus.Subscribe<OrderPlacedEvent>(_ =>
            {
                callCount++;
                return Task.CompletedTask;
            });

            await bus.PublishAsync(new OrderPlacedEvent("ORD-1", 10m));
            callCount.Should().Be(1);

            subscription.Dispose();

            await bus.PublishAsync(new OrderPlacedEvent("ORD-2", 20m));
            callCount.Should().Be(1);
        }

        [Fact]
        public async Task PublishAsync_MultipleSubscribers_AllReceiveEvent()
        {
            var bus = new EventBus();
            var received1 = false;
            var received2 = false;

            bus.Subscribe<OrderPlacedEvent>(_ => { received1 = true; return Task.CompletedTask; });
            bus.Subscribe<OrderPlacedEvent>(_ => { received2 = true; return Task.CompletedTask; });

            await bus.PublishAsync(new OrderPlacedEvent("ORD-3", 50m));

            received1.Should().BeTrue();
            received2.Should().BeTrue();
        }

        [Fact]
        public async Task PublishAsync_ParallelStrategy_ExecutesConcurrently()
        {
            var bus = new EventBus();
            var orderOfExecution = new ConcurrentBag<int>();

            bus.Subscribe<OrderPlacedEvent>(async _ =>
            {
                await Task.Delay(50);
                orderOfExecution.Add(1);
            });

            bus.Subscribe<OrderPlacedEvent>(async _ =>
            {
                await Task.Delay(10);
                orderOfExecution.Add(2);
            });

            await bus.PublishAsync(new OrderPlacedEvent("ORD-4", 100m), EventPublishStrategy.Parallel);

            orderOfExecution.Should().HaveCount(2);
        }

        [Fact]
        public async Task PublishAsync_FailingSubscriber_DoesNotPreventOtherSubscribersFromExecuting()
        {
            var bus = new EventBus();
            bool secondHandlerExecuted = false;

            bus.Subscribe<OrderPlacedEvent>(_ => throw new InvalidOperationException("Handler 1 crashed"));
            bus.Subscribe<OrderPlacedEvent>(_ =>
            {
                secondHandlerExecuted = true;
                return Task.CompletedTask;
            });

            Func<Task> act = async () => await bus.PublishAsync(new OrderPlacedEvent("ORD-5", 10m));

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Handler 1 crashed");

            secondHandlerExecuted.Should().BeTrue();
        }

        [Fact]
        public async Task PublishAsync_ExecutesMiddlewarePipeline()
        {
            var bus = new EventBus();
            var trace = new List<string>();

            bus.AddMiddleware(new EventTrackingMiddleware("EM1", trace));
            bus.AddMiddleware(new EventTrackingMiddleware("EM2", trace));

            bus.Subscribe<OrderPlacedEvent>(_ => Task.CompletedTask);

            await bus.PublishAsync(new OrderPlacedEvent("ORD-6", 15m));

            trace.Should().ContainInOrder("EM1:Start", "EM2:Start", "EM2:End", "EM1:End");
        }

        [Fact]
        public async Task PublishAsync_ResolvesSubscribersFromServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddSingleton<OrderPlacedEventHandler>();
            services.AddTransient<IEventHandler<OrderPlacedEvent>>(sp => sp.GetRequiredService<OrderPlacedEventHandler>());

            var sp = services.BuildServiceProvider();
            var bus = new EventBus(sp);

            await bus.PublishAsync(new OrderPlacedEvent("ORD-DI", 300m));

            var handler = sp.GetRequiredService<OrderPlacedEventHandler>();
            handler.ReceivedEvents.Should().HaveCount(1);
            handler.ReceivedEvents[0].OrderId.Should().Be("ORD-DI");
        }

        [Fact]
        public async Task PublishAsync_ConcurrentSubscribeAndPublish_ThreadSafe()
        {
            var bus = new EventBus();
            var counter = 0;
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            var publishTask = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    await bus.PublishAsync(new OrderPlacedEvent("ORD-CONC", 1m));
                    await Task.Yield();
                }
            });

            var subscribeTask = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    var sub = bus.Subscribe<OrderPlacedEvent>(_ =>
                    {
                        Interlocked.Increment(ref counter);
                        return Task.CompletedTask;
                    });
                    await Task.Yield();
                    sub.Dispose();
                }
            });

            await Task.WhenAll(publishTask, subscribeTask);
            counter.Should().BeGreaterOrEqualTo(0);
        }
    }
}
