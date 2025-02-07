using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// Defines a contract for handling commands of a specific type.
    /// </summary>
    /// <typeparam name="TCommand">The type of command that this handler processes. Must implement <see cref="ICommand"/>.</typeparam>
    public interface ICommandHandler<TCommand> where TCommand : ICommand
    {
        /// <summary>
        /// Asynchronously handles the given command.
        /// </summary>
        /// <param name="command">The command instance to be processed.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task HandleAsync(TCommand command);
    }
}
