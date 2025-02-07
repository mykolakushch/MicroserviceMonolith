using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Execution
{
    public class ExecutionQueue : IDisposable
    {
        private readonly BlockingCollection<Func<Task>> _queue = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _worker;

        public ExecutionQueue()
        {
            _worker = Task.Run(ProcessQueueAsync);
        }

        public void Enqueue(Func<Task> taskGenerator)
        {
            _queue.Add(taskGenerator);
        }

        private async Task ProcessQueueAsync()
        {
            foreach (var taskGenerator in _queue.GetConsumingEnumerable(_cts.Token))
            {
                try
                {
                    await taskGenerator();
                }
                catch
                {
                    // Обробка помилок за потреби
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _queue.CompleteAdding();
            try { _worker.Wait(); } catch { }
            _cts.Dispose();
            _queue.Dispose();
        }
    }
}
