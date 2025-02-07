using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// Defines a contract for a command bus responsible for dispatching commands to their corresponding handlers.
    /// </summary>
    public interface ICommandBus
    {
        /// <summary>
        /// Asynchronously sends a command to the appropriate handler for execution.
        /// </summary>
        /// <typeparam name="TCommand">The type of command to send. Must implement <see cref="ICommand"/>.</typeparam>
        /// <param name="command">The command instance to be processed.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task SendAsync<TCommand>(TCommand command) where TCommand : ICommand;
    }
}
