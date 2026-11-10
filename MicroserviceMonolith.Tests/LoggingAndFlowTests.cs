using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MicroserviceMonolith.Core.Commands;
using MicroserviceMonolith.Core.DependencyInjection;
using MicroserviceMonolith.Core.Execution;
using MicroserviceMonolith.Core.Modules;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class LoggingAndFlowTests
    {
        [Fact]
        public async Task ErrorHandlingMiddleware_LogsTheFullException_NotJustTheMessage()
        {
            var logger = new TestLogger();
            var middleware = new ErrorHandlingCommandMiddleware(logger);

            Func<Task<int>> failing = () => throw new InvalidOperationException("boom");
            Func<Task> act = () => middleware.InvokeAsync<CommandBusTests.TestResultCommand, int>(
                new CommandBusTests.TestResultCommand(0, 0),
                failing,
                CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>();
            var entry = logger.Entries.Should().ContainSingle().Subject;
            entry.Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Error);
            entry.Exception.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("boom");
        }

        [Fact]
        public void AddMicroserviceMonolith_WorksWithoutLogging_AndDoesNotRegisterALogger()
        {
            var services = new ServiceCollection();
            services.AddMicroserviceMonolith();
            using var provider = services.BuildServiceProvider();

            provider.GetRequiredService<ModuleManager>().Should().NotBeNull();
            provider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>().Should().BeNull();
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
