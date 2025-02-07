using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Execution
{
    /// <summary>
    /// A queue that processes tasks asynchronously in the order they are added.
    /// It uses a background worker to process tasks and ensures only one task is processed at a time.
    /// </summary>
    public class ExecutionQueue : IDisposable
    {
        private readonly BlockingCollection<Func<Task>> _queue = new();  // The collection that holds the tasks to be executed.
        private readonly CancellationTokenSource _cts = new();  // A cancellation token source used to cancel the worker task.
        private readonly Task _worker;  // The background worker task that processes the queue.

        /// <summary>
        /// Initializes a new instance of the <see cref="ExecutionQueue"/> class.
        /// Starts a background worker that processes tasks from the queue asynchronously.
        /// </summary>
        public ExecutionQueue()
        {
            _worker = Task.Run(ProcessQueueAsync);  // Start the worker task when the queue is instantiated.
        }

        /// <summary>
        /// Enqueues a new task to be processed by the queue.
        /// </summary>
        /// <param name="taskGenerator">A function that generates the task to be executed.</param>
        public void Enqueue(Func<Task> taskGenerator)
        {
            _queue.Add(taskGenerator);  // Add the task generator to the queue.
        }

        /// <summary>
        /// The background worker task that processes tasks from the queue asynchronously.
        /// It retrieves tasks from the queue and executes them one by one.
        /// </summary>
        private async Task ProcessQueueAsync()
        {
            foreach (var taskGenerator in _queue.GetConsumingEnumerable(_cts.Token))  // Continuously process tasks until cancellation.
            {
                try
                {
                    await taskGenerator();  // Execute the task asynchronously.
                }
                catch
                {
                    // Handle errors if needed (currently, errors are caught but not processed).
                }
            }
        }

        /// <summary>
        /// Disposes of the <see cref="ExecutionQueue"/> and cancels any ongoing task processing.
        /// It ensures that the worker task is properly awaited and that resources are cleaned up.
        /// </summary>
        public void Dispose()
        {
            _cts.Cancel();  // Cancel the worker task.
            _queue.CompleteAdding();  // Indicate that no more tasks will be added to the queue.
            try { _worker.Wait(); } catch { }  // Wait for the worker task to complete.
            _cts.Dispose();  // Dispose the cancellation token source.
            _queue.Dispose();  // Dispose the blocking collection.
        }
    }
}
