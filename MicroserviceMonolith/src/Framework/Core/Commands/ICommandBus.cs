using System.Threading;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// Defines a contract for a command bus responsible for dispatching commands to their corresponding handlers.
    /// </summary>
    public interface ICommandBus
    {
        /// <summary>
        /// Asynchronously sends a command without a return value to the appropriate handler for execution.
        /// </summary>
        /// <typeparam name="TCommand">The type of command to send. Must implement <see cref="ICommand"/>.</typeparam>
        /// <param name="command">The command instance to be processed.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand;

        /// <summary>
        /// Asynchronously sends a command that returns a result to the appropriate handler for execution.
        /// </summary>
        /// <typeparam name="TResult">The type of result returned by the command handler.</typeparam>
        /// <param name="command">The command instance to be processed.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task representing the asynchronous operation containing the result.</returns>
        Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously sends a command of a specific type that returns a result.
        /// </summary>
        /// <typeparam name="TCommand">The command type.</typeparam>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="command">The command instance.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task representing the asynchronous operation containing the result.</returns>
        Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand<TResult>;
    }
}
