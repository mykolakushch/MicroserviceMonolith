using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Commands
{
    public interface ICommandHandler<TCommand> where TCommand : ICommand
    {
        Task HandleAsync(TCommand command);
    }
}
