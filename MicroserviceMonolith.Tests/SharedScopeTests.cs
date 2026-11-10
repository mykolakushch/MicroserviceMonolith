using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Commands;
using MicroserviceMonolith.Core.DependencyInjection;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Core.Queries;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class SharedScopeTests
    {
        public record ScopeOuterCommand : ICommand<int>;

        public record ScopeInnerQuery : IQuery<int>;

        public record ScopeEvent : EventBase;

        public sealed class ScopedMarker : IDisposable
        {
            private readonly ScopeProbe _probe;

            public ScopedMarker(ScopeProbe probe) => _probe = probe;

            public Guid Id { get; } = Guid.NewGuid();

            public void Dispose() => _probe.DisposeCount++;
        }

        public sealed class ScopeProbe
        {
            public ConcurrentQueue<Guid> OuterIds { get; } = new();
            public ConcurrentQueue<Guid> InnerIds { get; } = new();
            public ConcurrentQueue<Guid> EventIds { get; } = new();
            public int DisposeCount;
            public int DisposeCountAfterNested = -1;
        }

        public sealed class ScopeOuterHandler : ICommandHandler<ScopeOuterCommand, int>
        {
            private readonly ScopedMarker _marker;
            private readonly IQueryBus _queries;
            private readonly IEventPublisher _events;
            private readonly ScopeProbe _probe;

            public ScopeOuterHandler(ScopedMarker marker, IQueryBus queries, IEventPublisher events, ScopeProbe probe)
            {
                _marker = marker;
                _queries = queries;
                _events = events;
                _probe = probe;
            }

            public async Task<int> HandleAsync(ScopeOuterCommand command, CancellationToken cancellationToken = default)
            {
                _probe.OuterIds.Enqueue(_marker.Id);
                await _queries.QueryAsync<ScopeInnerQuery, int>(new ScopeInnerQuery(), cancellationToken);
                await _events.PublishAsync(new ScopeEvent(), cancellationToken);
                _probe.DisposeCountAfterNested = _probe.DisposeCount;
                return 0;
            }
        }

        public sealed class ScopeInnerHandler : IQueryHandler<ScopeInnerQuery, int>
        {
            private readonly ScopedMarker _marker;
            private readonly ScopeProbe _probe;

            public ScopeInnerHandler(ScopedMarker marker, ScopeProbe probe)
            {
                _marker = marker;
                _probe = probe;
            }

            public Task<int> HandleAsync(ScopeInnerQuery query, CancellationToken cancellationToken = default)
            {
                _probe.InnerIds.Enqueue(_marker.Id);
                return Task.FromResult(0);
            }
        }

        public sealed class ScopeEventHandler : IEventHandler<ScopeEvent>
        {
            private readonly ScopedMarker _marker;
            private readonly ScopeProbe _probe;

            public ScopeEventHandler(ScopedMarker marker, ScopeProbe probe)
            {
                _marker = marker;
                _probe = probe;
            }

            public Task HandleAsync(ScopeEvent @event, CancellationToken cancellationToken = default)
            {
                _probe.EventIds.Enqueue(_marker.Id);
                return Task.CompletedTask;
            }
        }

        private static ServiceProvider Build(bool share)
        {
            var services = new ServiceCollection();
            services.AddMicroserviceMonolith(o =>
            {
                o.ShareScopeBetweenNestedDispatches = share;
                o.AutoProcessOutbox = false;
            });
            services.AddSingleton<ScopeProbe>();
            services.AddScoped<ScopedMarker>();
            services.AddTransient<ICommandHandler<ScopeOuterCommand, int>, ScopeOuterHandler>();
            services.AddTransient<IQueryHandler<ScopeInnerQuery, int>, ScopeInnerHandler>();
            services.AddTransient<IEventHandler<ScopeEvent>, ScopeEventHandler>();
            return services.BuildServiceProvider();
        }

        [Fact]
        public async Task ByDefault_EveryDispatchGetsItsOwnScope()
        {
            using var provider = Build(share: false);
            var probe = provider.GetRequiredService<ScopeProbe>();

            await provider.GetRequiredService<ICommandBus>().SendAsync<ScopeOuterCommand, int>(new ScopeOuterCommand());

            probe.InnerIds.Should().ContainSingle().Which.Should().NotBe(probe.OuterIds.ToArray()[0]);
            probe.EventIds.Should().ContainSingle().Which.Should().NotBe(probe.OuterIds.ToArray()[0]);
        }

        [Fact]
        public async Task WhenEnabled_NestedQueryAndEventShareTheOutermostScope()
        {
            using var provider = Build(share: true);
            var probe = provider.GetRequiredService<ScopeProbe>();

            await provider.GetRequiredService<ICommandBus>().SendAsync<ScopeOuterCommand, int>(new ScopeOuterCommand());

            var outer = probe.OuterIds.ToArray()[0];
            probe.InnerIds.Should().ContainSingle().Which.Should().Be(outer);
            probe.EventIds.Should().ContainSingle().Which.Should().Be(outer);
        }

        [Fact]
        public async Task WhenEnabled_TheScopeIsDisposedOnceByTheOutermostDispatch_AfterNestedOnesFinished()
        {
            using var provider = Build(share: true);
            var probe = provider.GetRequiredService<ScopeProbe>();

            await provider.GetRequiredService<ICommandBus>().SendAsync<ScopeOuterCommand, int>(new ScopeOuterCommand());

            probe.DisposeCountAfterNested.Should().Be(0); // nested dispatches did not dispose the shared scope
            probe.DisposeCount.Should().Be(1);
        }

        [Fact]
        public async Task WhenEnabled_SeparateTopLevelDispatchesStillGetSeparateScopes()
        {
            using var provider = Build(share: true);
            var probe = provider.GetRequiredService<ScopeProbe>();
            var bus = provider.GetRequiredService<ICommandBus>();

            await bus.SendAsync<ScopeOuterCommand, int>(new ScopeOuterCommand());
            await bus.SendAsync<ScopeOuterCommand, int>(new ScopeOuterCommand());

            var ids = probe.OuterIds.ToArray();
            ids.Should().HaveCount(2);
            ids[0].Should().NotBe(ids[1]);
        }
    }
}
