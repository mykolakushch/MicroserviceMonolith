using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Commands;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Core.Logging;
using MicroserviceMonolith.Core.Modules;
using MicroserviceMonolith.Core.Outbox;
using Xunit;
using FrameworkLogger = MicroserviceMonolith.Core.Logging.ILogger;

namespace MicroserviceMonolith.Tests
{
    /// <summary>Discovered by ConfigureModules although it is not public.</summary>
    internal sealed class InternalDiscoveredModule : ModuleBase
    {
    }

    public class SeverityAndLifecycleTests
    {
        private sealed class SeverityLogger : FrameworkLogger
        {
            public ConcurrentQueue<(LogSeverity Severity, string Message)> Entries { get; } = new();

            public void Log(string message) => Entries.Enqueue((LogSeverity.Information, message));

            public void Log(LogSeverity severity, string message) => Entries.Enqueue((severity, message));
        }

        private sealed class MessageOnlyLogger : FrameworkLogger
        {
            public List<string> Messages { get; } = new();

            public void Log(string message) => Messages.Add(message);
        }

        private sealed class CapturingMelLogger : Microsoft.Extensions.Logging.ILogger
        {
            public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

            public void Log<TState>(
                Microsoft.Extensions.Logging.LogLevel logLevel,
                Microsoft.Extensions.Logging.EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => Entries.Add((logLevel, formatter(state, exception)));
        }

        private sealed class NamedModule : ModuleBase
        {
            private readonly string _label;
            private readonly List<string> _events;

            public NamedModule(string label, List<string> events)
            {
                _label = label;
                _events = events;
            }

            public override Task InitializeAsync(IServiceProvider sp, CancellationToken ct)
            {
                _events.Add($"{_label}:Init");
                return Task.CompletedTask;
            }

            public override Task ShutdownAsync(IServiceProvider sp, CancellationToken ct)
            {
                _events.Add($"{_label}:Shutdown");
                return Task.CompletedTask;
            }
        }

        [Fact]
        public void SeverityOverload_FallsBackToLogString_ForLoggersThatOnlyImplementIt()
        {
            var logger = new MessageOnlyLogger();
            FrameworkLogger asInterface = logger;

            asInterface.Log(LogSeverity.Error, "something failed");

            logger.Messages.Should().Equal("something failed");
        }

        [Theory]
        [InlineData(LogSeverity.Debug, Microsoft.Extensions.Logging.LogLevel.Debug)]
        [InlineData(LogSeverity.Information, Microsoft.Extensions.Logging.LogLevel.Information)]
        [InlineData(LogSeverity.Warning, Microsoft.Extensions.Logging.LogLevel.Warning)]
        [InlineData(LogSeverity.Error, Microsoft.Extensions.Logging.LogLevel.Error)]
        public void MicrosoftLoggingAdapter_MapsSeverityToLogLevel(LogSeverity severity, Microsoft.Extensions.Logging.LogLevel expected)
        {
            var mel = new CapturingMelLogger();
            FrameworkLogger adapter = new MicrosoftLoggingAdapter(mel);

            adapter.Log(severity, "hello");

            mel.Entries.Should().ContainSingle().Which.Should().Be((expected, "hello"));
        }

        [Fact]
        public async Task LoggingMiddleware_LogsAShortFailureLine_AtErrorSeverity_ErrorHandlingLogsTheFullException()
        {
            var logger = new SeverityLogger();
            var logging = new LoggingCommandMiddleware((FrameworkLogger)logger);
            var errors = new ErrorHandlingCommandMiddleware((FrameworkLogger)logger);
            var command = new CommandBusTests.TestResultCommand(0, 0);
            Func<Task<int>> failing = () => throw new InvalidOperationException("boom");

            Func<Task> act = () => errors.InvokeAsync<CommandBusTests.TestResultCommand, int>(
                command,
                () => logging.InvokeAsync<CommandBusTests.TestResultCommand, int>(command, failing, CancellationToken.None),
                CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>();

            var entries = logger.Entries.ToArray();
            var failed = entries.Single(e => e.Message.Contains("Failed"));
            failed.Severity.Should().Be(LogSeverity.Error);
            failed.Message.Should().Contain("InvalidOperationException: boom").And.NotContain(Environment.NewLine);

            var full = entries.Single(e => e.Message.Contains("Error in"));
            full.Severity.Should().Be(LogSeverity.Error);
        }

        [Fact]
        public async Task OutboxProcessor_LogsFailureAndDeadLettering_AtErrorSeverity()
        {
            var store = new InMemoryOutboxStore { MaxRetries = 1, RetryBaseDelay = TimeSpan.Zero };
            var bus = new EventBus();
            bus.Subscribe<OutboxReliabilityTests.ReliabilityEvent>(_ => throw new InvalidOperationException("handler failed"));
            var logger = new SeverityLogger();
            var processor = new OutboxProcessor(store, bus, null, (FrameworkLogger)logger);
            await store.SaveAsync(new OutboxMessage
            {
                EventType = nameof(OutboxReliabilityTests.ReliabilityEvent),
                Event = new OutboxReliabilityTests.ReliabilityEvent("a")
            });

            await processor.ProcessBatchAsync();

            var entries = logger.Entries.ToArray();
            entries.Should().Contain(e => e.Severity == LogSeverity.Error && e.Message.Contains("Failed to process message"));
            entries.Should().Contain(e => e.Severity == LogSeverity.Error && e.Message.Contains("dead-lettered"));
        }

        [Fact]
        public async Task Shutdown_SkipsModulesThatWereNeverInitialized()
        {
            var events = new List<string>();
            var manager = new ModuleManager();
            manager.AddModule(new NamedModule("A", events));
            manager.AddModule(new NamedModule("B", events));
            var sp = new ServiceCollection().BuildServiceProvider();
            await manager.InitializeModulesAsync(sp);

            manager.AddModule(new NamedModule("Late", events)); // added after startup, never initialized
            await manager.ShutdownModulesAsync(sp);

            events.Should().Equal("A:Init", "B:Init", "B:Shutdown", "A:Shutdown");
        }

        [Fact]
        public async Task Shutdown_WithoutInitialize_DoesNothing()
        {
            var events = new List<string>();
            var manager = new ModuleManager();
            manager.AddModule(new NamedModule("A", events));

            await manager.ShutdownModulesAsync(new ServiceCollection().BuildServiceProvider());

            events.Should().BeEmpty();
        }

        [Fact]
        public void ConfigureModules_DiscoversNonPublicModules_LikeAssemblyScanning()
        {
            var manager = new ModuleManager();

            manager.ConfigureModules(new ServiceCollection(), new ConfigurationBuilder().Build(), typeof(SeverityAndLifecycleTests).Assembly);

            manager.Modules.Should().Contain(m => m is InternalDiscoveredModule);
        }
    }
}
