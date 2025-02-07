using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Commands
{
    public interface ICommandBus
    {
        Task SendAsync<TCommand>(TCommand command) where TCommand : ICommand;
    }
}
