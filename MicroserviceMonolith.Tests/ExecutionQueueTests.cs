using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
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

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }
        }

        [Fact]
        public async Task Enqueue_WhenBoundedWaitQueueIsFull_ThrowsInsteadOfBufferingUnboundedly()
        {
            var options = new ExecutionQueueOptions { Capacity = 1, FullMode = BoundedChannelFullMode.Wait };
            await using var queue = new ExecutionQueue(options);

            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var executed = new ConcurrentQueue<int>();

            // Occupy the single worker so the next item stays in the channel buffer.
            queue.Enqueue(async _ =>
            {
                started.TrySetResult(true);
                await gate.Task;
                executed.Enqueue(1);
            });
            (await Task.WhenAny(started.Task, Task.Delay(2000))).Should().Be(started.Task);

            // Fills the single buffer slot.
            queue.Enqueue(_ =>
            {
                executed.Enqueue(2);
                return Task.CompletedTask;
            });

            // Queue is full: the item must be rejected, not silently scheduled elsewhere.
            Action overflow = () => queue.Enqueue(_ =>
            {
                executed.Enqueue(3);
                return Task.CompletedTask;
            });
            overflow.Should().Throw<InvalidOperationException>();

            gate.TrySetResult(true);
            await WaitUntilAsync(() => executed.Count >= 2);
            await Task.Delay(100); // a wrongly buffered third item would have run by now

            executed.ToArray().Should().Equal(1, 2);
        }

        [Fact]
        public async Task EnqueueAsync_WhenBoundedWaitQueueIsFull_WaitsForCapacityAndPreservesOrder()
        {
            var options = new ExecutionQueueOptions { Capacity = 1, FullMode = BoundedChannelFullMode.Wait };
            await using var queue = new ExecutionQueue(options);

            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var executed = new ConcurrentQueue<int>();

            queue.Enqueue(async _ =>
            {
                started.TrySetResult(true);
                await gate.Task;
                executed.Enqueue(1);
            });
            (await Task.WhenAny(started.Task, Task.Delay(2000))).Should().Be(started.Task);

            queue.Enqueue(_ =>
            {
                executed.Enqueue(2);
                return Task.CompletedTask;
            });

            var pending = queue.EnqueueAsync(_ =>
            {
                executed.Enqueue(3);
                return Task.CompletedTask;
            });
            pending.IsCompleted.Should().BeFalse(); // producer is held back by backpressure

            gate.TrySetResult(true);
            await pending.AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            await WaitUntilAsync(() => executed.Count >= 3);

            executed.ToArray().Should().Equal(1, 2, 3);
        }

        [Fact]
        public async Task Enqueue_AfterDispose_ThrowsObjectDisposedException()
        {
            var queue = new ExecutionQueue();
            await queue.DisposeAsync();

            Action act = () => queue.Enqueue(_ => Task.CompletedTask);

            act.Should().Throw<ObjectDisposedException>();
        }
    }
}
