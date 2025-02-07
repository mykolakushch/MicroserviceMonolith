using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Events
{
    public interface IEventPublisher
    {
        Task PublishAsync<TEvent>(TEvent @event) where TEvent : class;
    }
}
