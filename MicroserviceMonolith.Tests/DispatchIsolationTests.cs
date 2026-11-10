using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Commands;
using MicroserviceMonolith.Core.Context;
using MicroserviceMonolith.Core.DependencyInjection;
using MicroserviceMonolith.Core.Events;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class DispatchIsolationTests
    {
        public record ChargeCommand : ICommand<int>;

        [IdempotentCommand]
        public record SetStatusCommand : ICommand<int>;

        public record IsolationProbeEvent : EventBase;

        public record ProbeCommand : ICommand<int>;

        public sealed class IsoProbe
        {
            public ConcurrentQueue<Guid> MarkerIds { get; } = new();
            public int AsyncDisposals;
        }

        public sealed class IsoMarker
        {
            public Guid Id { get; } = Guid.NewGuid();
        }

        /// <summary>Only implements IAsyncDisposable: a synchronous scope disposal would throw.</summary>
        public sealed class AsyncOnlyDisposable : IAsyncDisposable
        {
            private readonly IsoProbe _probe;

            public AsyncOnlyDisposable(IsoProbe probe) => _probe = probe;

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref _probe.AsyncDisposals);
                return ValueTask.CompletedTask;
            }
        }

        public sealed class IsoHandlerOne : IEventHandler<IsolationProbeEvent>
        {
            private readonly IsoMarker _marker;
            private readonly IsoProbe _probe;

            public IsoHandlerOne(IsoMarker marker, AsyncOnlyDisposable asyncOnly, IsoProbe probe)
            {
                _marker = marker;
                _probe = probe;
            }

            public async Task HandleAsync(IsolationProbeEvent @event, CancellationToken cancellationToken = default)
            {
                await Task.Yield();
                _probe.MarkerIds.Enqueue(_marker.Id);
            }
        }

        public sealed class IsoHandlerTwo : IEventHandler<IsolationProbeEvent>
        {
            private readonly IsoMarker _marker;
            private readonly IsoProbe _probe;

            public IsoHandlerTwo(IsoMarker marker, AsyncOnlyDisposable asyncOnly, IsoProbe probe)
            {
                _marker = marker;
                _probe = probe;
            }

            public async Task HandleAsync(IsolationProbeEvent @event, CancellationToken cancellationToken = default)
            {
                await Task.Yield();
                _probe.MarkerIds.Enqueue(_marker.Id);
            }
        }

        private sealed class ProbeCommandHandler : ICommandHandler<ProbeCommand, int>
        {
            private readonly ICorrelationContextAccessor _accessor;

            public ProbeCommandHandler(ICorrelationContextAccessor accessor) => _accessor = accessor;

            public string? SeenCorrelationId { get; private set; }

            public Task<int> HandleAsync(ProbeCommand command, CancellationToken cancellationToken = default)
            {
                SeenCorrelationId = _accessor.CorrelationContext?.CorrelationId;
                return Task.FromResult(1);
            }
        }

        /// <summary>A plain-field accessor (not AsyncLocal): nothing restores it automatically.</summary>
        private sealed class FieldAccessor : ICorrelationContextAccessor
        {
            public CorrelationContext? CorrelationContext { get; set; }
        }

        private static ServiceProvider Build()
        {
            var services = new ServiceCollection();
            services.AddMicroserviceMonolith(o => o.AutoProcessOutbox = false);
            services.AddSingleton<IsoProbe>();
            services.AddScoped<IsoMarker>();
            services.AddScoped<AsyncOnlyDisposable>();
            services.AddTransient<IEventHandler<IsolationProbeEvent>, IsoHandlerOne>();
            services.AddTransient<IEventHandler<IsolationProbeEvent>, IsoHandlerTwo>();
            return services.BuildServiceProvider();
        }

        [Fact]
        public async Task Sequential_HandlersShareOneScope()
        {
            using var provider = Build();
            var probe = provider.GetRequiredService<IsoProbe>();

            await provider.GetRequiredService<IEventPublisher>().PublishAsync(new IsolationProbeEvent(), EventPublishStrategy.Sequential);

            probe.MarkerIds.Should().HaveCount(2);
            probe.MarkerIds.Distinct().Should().HaveCount(1);
        }

        [Fact]
        public async Task Parallel_EveryHandlerGetsItsOwnScope_AndScopesAreDisposedAsynchronously()
        {
            using var provider = Build();
            var probe = provider.GetRequiredService<IsoProbe>();

            await provider.GetRequiredService<IEventPublisher>().PublishAsync(new IsolationProbeEvent(), EventPublishStrategy.Parallel);

            probe.MarkerIds.Should().HaveCount(2);
            probe.MarkerIds.Distinct().Should().HaveCount(2); // no scoped service is shared between concurrent handlers
            probe.AsyncDisposals.Should().Be(2);              // async-only disposables did not break scope disposal
        }

        [Fact]
        public async Task Sequential_ScopeWithAsyncOnlyDisposable_IsDisposedWithoutThrowing()
        {
            using var provider = Build();
            var probe = provider.GetRequiredService<IsoProbe>();

            await provider.GetRequiredService<IEventPublisher>().PublishAsync(new IsolationProbeEvent());

            probe.AsyncDisposals.Should().Be(1);
        }

        [Fact]
        public async Task Parallel_WithSharedScopeOptionEnabled_StillIsolatesHandlers()
        {
            var services = new ServiceCollection();
            services.AddMicroserviceMonolith(o =>
            {
                o.AutoProcessOutbox = false;
                o.ShareScopeBetweenNestedDispatches = true;
            });
            services.AddSingleton<IsoProbe>();
            services.AddScoped<IsoMarker>();
            services.AddScoped<AsyncOnlyDisposable>();
            services.AddTransient<IEventHandler<IsolationProbeEvent>, IsoHandlerOne>();
            services.AddTransient<IEventHandler<IsolationProbeEvent>, IsoHandlerTwo>();
            using var provider = services.BuildServiceProvider();
            var probe = provider.GetRequiredService<IsoProbe>();

            await provider.GetRequiredService<IEventPublisher>().PublishAsync(new IsolationProbeEvent(), EventPublishStrategy.Parallel);

            probe.MarkerIds.Distinct().Should().HaveCount(2);
        }

        [Fact]
        public async Task CommandBus_RestoresCorrelationContext_ItCreated()
        {
            var accessor = new FieldAccessor();
            var handler = new ProbeCommandHandler(accessor);
            var bus = new CommandBus(null, accessor);
            bus.RegisterHandler<ProbeCommand, int>(handler);

            await bus.SendAsync<ProbeCommand, int>(new ProbeCommand());

            handler.SeenCorrelationId.Should().NotBeNullOrEmpty(); // a context existed during the dispatch
            accessor.CorrelationContext.Should().BeNull();         // and the bus took it away again
        }

        [Fact]
        public async Task CommandBus_KeepsAnExistingCorrelationContext()
        {
            var accessor = new FieldAccessor { CorrelationContext = new CorrelationContext("outer-1") };
            var handler = new ProbeCommandHandler(accessor);
            var bus = new CommandBus(null, accessor);
            bus.RegisterHandler<ProbeCommand, int>(handler);

            await bus.SendAsync<ProbeCommand, int>(new ProbeCommand());

            handler.SeenCorrelationId.Should().Be("outer-1");
            accessor.CorrelationContext!.CorrelationId.Should().Be("outer-1");
        }

        private static async Task<int> CountAttemptsAsync<TCommand>(RetryCommandMiddleware middleware, TCommand command, int failuresBeforeSuccess)
            where TCommand : ICommand<int>
        {
            var attempts = 0;
            try
            {
                await middleware.InvokeAsync<TCommand, int>(command, () =>
                {
                    attempts++;
                    if (attempts <= failuresBeforeSuccess) throw new TimeoutException("flaky");
                    return Task.FromResult(attempts);
                });
            }
            catch (TimeoutException)
            {
            }

            return attempts;
        }

        [Fact]
        public async Task Retry_ByDefault_DoesNotRetryCommandsThatAreNotMarkedIdempotent()
        {
            var middleware = new RetryCommandMiddleware(3, TimeSpan.Zero, 1.0, false);

            var attempts = await CountAttemptsAsync(middleware, new ChargeCommand(), failuresBeforeSuccess: 2);

            attempts.Should().Be(1); // the failure is propagated, the charge is not repeated
        }

        [Fact]
        public async Task Retry_RetriesCommandsMarkedIdempotent()
        {
            var middleware = new RetryCommandMiddleware(3, TimeSpan.Zero, 1.0, false);

            var attempts = await CountAttemptsAsync(middleware, new SetStatusCommand(), failuresBeforeSuccess: 2);

            attempts.Should().Be(3);
        }

        [Fact]
        public async Task Retry_WithExplicitOptIn_RetriesEveryCommand()
        {
            var middleware = new RetryCommandMiddleware(3, TimeSpan.Zero, 1.0, false, retryNonIdempotentCommands: true);

            var attempts = await CountAttemptsAsync(middleware, new ChargeCommand(), failuresBeforeSuccess: 2);

            attempts.Should().Be(3);
        }
    }
}
