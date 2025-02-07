using System;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Events
{
    public interface IEventMiddleware
    {
        Task InvokeAsync<TEvent>(TEvent @event, Func<Task> next) where TEvent : class;
    }
}
