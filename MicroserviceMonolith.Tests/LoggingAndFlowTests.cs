using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Commands;
using MicroserviceMonolith.Core.DependencyInjection;
using MicroserviceMonolith.Core.Execution;
using MicroserviceMonolith.Core.Logging;
using Xunit;
using FrameworkLogger = MicroserviceMonolith.Core.Logging.ILogger;

namespace MicroserviceMonolith.Tests
{
    public class LoggingAndFlowTests
    {
        private sealed class ListLogger : FrameworkLogger
        {
            public List<string> Messages { get; } = new();

            public void Log(string message) => Messages.Add(message);
        }

        [Fact]
        public async Task ErrorHandlingMiddleware_LogsTheFullException_NotJustTheMessage()
        {
            var logger = new ListLogger();
            var middleware = new ErrorHandlingCommandMiddleware((FrameworkLogger)logger);

            Func<Task<int>> failing = () => throw new InvalidOperationException("boom");
            Func<Task> act = () => middleware.InvokeAsync<CommandBusTests.TestResultCommand, int>(
                new CommandBusTests.TestResultCommand(0, 0),
                failing,
                CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>();
            logger.Messages.Should().ContainSingle()
                .Which.Should().Contain("System.InvalidOperationException").And.Contain("boom");
        }

        [Fact]
        public void Logger_UsesMicrosoftLogging_WhenALoggerFactoryIsRegistered()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMicroserviceMonolith();
            using var provider = services.BuildServiceProvider();

            provider.GetRequiredService<FrameworkLogger>().Should().BeOfType<MicrosoftLoggingAdapter>();
        }

        [Fact]
        public void Logger_FallsBackToConsole_WithoutALoggerFactory()
        {
            var services = new ServiceCollection();
            services.AddMicroserviceMonolith();
            using var provider = services.BuildServiceProvider();

            provider.GetRequiredService<FrameworkLogger>().Should().BeOfType<ConsoleLogger>();
        }

        [Fact]
        public async Task ExecutionQueueWorkers_DoNotCaptureTheCreatorsAsyncLocalState()
        {
            var ambient = new AsyncLocal<string?>();
            ambient.Value = "creator-context";

            using var queue = new ExecutionQueue();
            var seen = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue(() =>
            {
                seen.TrySetResult(ambient.Value);
                return Task.CompletedTask;
            });

            (await seen.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeNull();
            ambient.Value.Should().Be("creator-context"); // the caller's own flow is restored
        }
    }
}
