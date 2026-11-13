using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Events;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class EventRetryScopeTests
    {
        public record RetryScopeEvent(string Id) : EventBase;

        private sealed class ScopedProbe : IDisposable
        {
            public Guid Id { get; } = Guid.NewGuid();
            public bool Disposed { get; private set; }
            public void Dispose() => Disposed = true;
        }

        private sealed class Recorder
        {
            public ConcurrentBag<(string Handler, Guid Scope, bool Disposed)> Attempts { get; } = new();
        }

        private abstract class FlakyHandler : IEventHandler<RetryScopeEvent>
        {
            private readonly Recorder _recorder;
            private readonly ScopedProbe _probe;

            protected FlakyHandler(Recorder recorder, ScopedProbe probe)
            {
                _recorder = recorder;
                _probe = probe;
            }

            protected abstract string Name { get; }
            protected abstract int FailuresBeforeSuccess { get; }

            public Task HandleAsync(RetryScopeEvent @event, CancellationToken cancellationToken = default)
            {
                _recorder.Attempts.Add((Name, _probe.Id, _probe.Disposed));
                var attempt = _recorder.Attempts.Count(a => a.Handler == Name);
                if (attempt <= FailuresBeforeSuccess)
                {
                    throw new InvalidOperationException($"{Name} attempt {attempt} failed");
                }

                return Task.CompletedTask;
            }
        }

        private sealed class FirstHandler : FlakyHandler
        {
            public FirstHandler(Recorder recorder, ScopedProbe probe) : base(recorder, probe) { }
            protected override string Name => "first";
            protected override int FailuresBeforeSuccess => 2;
        }

        private sealed class SecondHandler : FlakyHandler
        {
            public SecondHandler(Recorder recorder, ScopedProbe probe) : base(recorder, probe) { }
            protected override string Name => "second";
            protected override int FailuresBeforeSuccess => 0;
        }

        private sealed class ThirdHandler : FlakyHandler
        {
            public ThirdHandler(Recorder recorder, ScopedProbe probe) : base(recorder, probe) { }
            protected override string Name => "third";
            protected override int FailuresBeforeSuccess => 2;
        }

        private static (EventBus Bus, Recorder Recorder, ServiceProvider Provider) Build()
        {
            var services = new ServiceCollection();
            services.AddSingleton<Recorder>();
            services.AddScoped<ScopedProbe>();
            services.AddTransient<IEventHandler<RetryScopeEvent>, FirstHandler>();
            services.AddTransient<IEventHandler<RetryScopeEvent>, SecondHandler>();
            services.AddTransient<IEventHandler<RetryScopeEvent>, ThirdHandler>();
            var provider = services.BuildServiceProvider();

            var bus = new EventBus(provider);
            bus.AddHandlerMiddleware(new RetryEventMiddleware(3, TimeSpan.Zero, 2.0, false));
            return (bus, provider.GetRequiredService<Recorder>(), provider);
        }

        [Fact]
        public async Task Parallel_RetryOfAnyHandler_RunsInAFreshScope()
        {
            var (bus, recorder, provider) = Build();
            using (provider)
            {
                await bus.PublishAsync(new RetryScopeEvent("1"), EventPublishStrategy.Parallel);

                // The first and the third handler fail twice and succeed on the third attempt, the second runs once
                recorder.Attempts.Count(a => a.Handler == "first").Should().Be(3);
                recorder.Attempts.Count(a => a.Handler == "second").Should().Be(1);
                recorder.Attempts.Count(a => a.Handler == "third").Should().Be(3);

                // Every attempt ran in a live scope of its own: no attempt saw a disposed scope or the scope of another attempt
                recorder.Attempts.Should().OnlyContain(a => !a.Disposed);
                recorder.Attempts.Select(a => a.Scope).Distinct().Should().HaveCount(recorder.Attempts.Count);
            }
        }

        [Fact]
        public async Task Parallel_RetryExhausted_StillReportsFailureAndDoesNotRepeatOtherHandlers()
        {
            var (bus, recorder, provider) = Build();
            using (provider)
            {
                // Only one retry for each handler: "first" and "third" need two
                var limited = new EventBus(provider);
                limited.AddHandlerMiddleware(new RetryEventMiddleware(1, TimeSpan.Zero, 2.0, false));

                Func<Task> act = () => limited.PublishAsync(new RetryScopeEvent("1"), EventPublishStrategy.Parallel);

                var thrown = (await act.Should().ThrowAsync<AggregateException>()).Which;
                thrown.InnerExceptions.Should().HaveCount(2);
                recorder.Attempts.Count(a => a.Handler == "second").Should().Be(1);
                recorder.Attempts.Should().OnlyContain(a => !a.Disposed);
            }
        }
    }
}
