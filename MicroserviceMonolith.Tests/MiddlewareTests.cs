using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MicroserviceMonolith.Core.Commands;
using MicroserviceMonolith.Core.Events;
using MicroserviceMonolith.Core.Logging;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class MiddlewareTests
    {
        public record FlakyCommand : ICommand;
        public record FlakyEvent : EventBase;

        private class FakeLogger : ILogger
        {
            public List<string> Messages { get; } = new();
            public void Log(string message) => Messages.Add(message);
        }

        [Fact]
        public async Task RetryCommandMiddleware_RetriesUntilSuccess()
        {
            var logger = new FakeLogger();
            var middleware = new RetryCommandMiddleware(3, TimeSpan.FromMilliseconds(5), 1.0, false, null, logger.Log);

            int attempts = 0;
            Func<Task<int>> failingOperation = () =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new InvalidOperationException($"Attempt {attempts} failed");
                }
                return Task.FromResult(42);
            };

            var result = await middleware.InvokeAsync(
                new CommandBusTests.TestResultCommand(0, 0),
                failingOperation,
                CancellationToken.None);

            result.Should().Be(42);
            attempts.Should().Be(3);
            logger.Messages.Should().HaveCount(2); // 2 failed attempts logged
        }

        [Fact]
        public async Task RetryCommandMiddleware_ExceedingMaxRetries_ThrowsException()
        {
            var logger = new FakeLogger();
            var middleware = new RetryCommandMiddleware(2, TimeSpan.FromMilliseconds(5), 1.0, false, null, logger.Log);

            int attempts = 0;
            Func<Task<int>> alwaysFailing = () =>
            {
                attempts++;
                throw new InvalidOperationException("Always fails");
            };

            Func<Task> act = async () => await middleware.InvokeAsync(
                new CommandBusTests.TestResultCommand(0, 0),
                alwaysFailing,
                CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Always fails");

            attempts.Should().Be(3); // 1 initial + 2 retries = 3
        }

        [Fact]
        public async Task PerformanceCommandMiddleware_LogsWarningWhenExceedsThreshold()
        {
            var logger = new FakeLogger();
            var middleware = new PerformanceCommandMiddleware(TimeSpan.FromMilliseconds(20), logger);

            await middleware.InvokeAsync(
                new CommandBusTests.TestResultCommand(1, 2),
                async () =>
                {
                    await Task.Delay(40);
                    return 3;
                },
                CancellationToken.None);

            logger.Messages.Should().ContainSingle(m => m.Contains("took") && m.Contains("Performance Warning"));
        }

        [Fact]
        public async Task ErrorHandlingCommandMiddleware_LogsErrorAndRethrows()
        {
            var logger = new FakeLogger();
            var middleware = new ErrorHandlingCommandMiddleware(logger);

            Func<Task> act = async () => await middleware.InvokeAsync<CommandBusTests.TestResultCommand, int>(
                new CommandBusTests.TestResultCommand(1, 2),
                () => throw new ApplicationException("Boom"),
                CancellationToken.None);

            await act.Should().ThrowAsync<ApplicationException>().WithMessage("Boom");
            logger.Messages.Should().ContainSingle(m => m.Contains("Boom"));
        }
    }
}
