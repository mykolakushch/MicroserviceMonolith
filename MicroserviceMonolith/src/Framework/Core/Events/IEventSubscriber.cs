using System;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Events
{
    public interface IEventSubscriber
    {
        IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : class;
    }
}
