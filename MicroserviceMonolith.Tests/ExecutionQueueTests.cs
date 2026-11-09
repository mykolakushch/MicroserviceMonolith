using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MicroserviceMonolith.Core.Execution;
using Xunit;

namespace MicroserviceMonolith.Tests
{
    public class ExecutionQueueTests
    {
        [Fact]
        public async Task Enqueue_ExecutesTasksInOrder()
        {
            await using var queue = new ExecutionQueue();
            var results = new ConcurrentQueue<int>();
            var tcs = new TaskCompletionSource<bool>();

            for (int i = 1; i <= 5; i++)
            {
                int val = i;
                queue.Enqueue(async () =>
                {
                    results.Enqueue(val);
                    if (val == 5) tcs.TrySetResult(true);
                    await Task.CompletedTask;
                });
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
            completed.Should().Be(tcs.Task);

            results.ToArray().Should().Equal(1, 2, 3, 4, 5);
        }

        [Fact]
        public async Task ExecutionQueue_FailingTask_RaisesOnErrorEventAndContinues()
        {
            await using var queue = new ExecutionQueue();
            Exception? caughtException = null;
            var errorRaisedTcs = new TaskCompletionSource<bool>();
            var subsequentTaskTcs = new TaskCompletionSource<bool>();

            queue.OnError += (_, args) =>
            {
                caughtException = args.Exception;
                errorRaisedTcs.TrySetResult(true);
            };

            queue.Enqueue(() => throw new ApplicationException("Background task failure"));

            queue.Enqueue(async () =>
            {
                subsequentTaskTcs.TrySetResult(true);
                await Task.CompletedTask;
            });

            var errorFinished = await Task.WhenAny(errorRaisedTcs.Task, Task.Delay(2000));
            errorFinished.Should().Be(errorRaisedTcs.Task);
            caughtException.Should().NotBeNull();
            caughtException!.Message.Should().Be("Background task failure");

            var subsequentFinished = await Task.WhenAny(subsequentTaskTcs.Task, Task.Delay(2000));
            subsequentFinished.Should().Be(subsequentTaskTcs.Task);
        }

        [Fact]
        public async Task ExecutionQueue_MultipleWorkers_ProcessesConcurrently()
        {
            var options = new ExecutionQueueOptions
            {
                WorkerCount = 3
            };

            await using var queue = new ExecutionQueue(options);
            var counter = 0;
            var tcs = new TaskCompletionSource<bool>();

            for (int i = 0; i < 6; i++)
            {
                queue.Enqueue(async () =>
                {
                    await Task.Delay(50);
                    if (Interlocked.Increment(ref counter) == 6)
                    {
                        tcs.TrySetResult(true);
                    }
                });
            }

            var finished = await Task.WhenAny(tcs.Task, Task.Delay(3000));
            finished.Should().Be(tcs.Task);
            counter.Should().Be(6);
        }
    }
}
