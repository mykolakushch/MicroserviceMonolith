using System.Threading;
using System.Threading.Tasks;
using MicroserviceMonolith.Core.Common;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// Defines a contract for handling commands that return a result of type <typeparamref name="TResult"/>.
    /// </summary>
    /// <typeparam name="TCommand">The type of command that this handler processes. Must implement <see cref="ICommand{TResult}"/>.</typeparam>
    /// <typeparam name="TResult">The type of result produced by handling the command.</typeparam>
    public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
    {
        /// <summary>
        /// Asynchronously handles the given command.
        /// </summary>
        /// <param name="command">The command instance to be processed.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task representing the asynchronous operation with the result.</returns>
        Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Defines a contract for handling commands of a specific type that do not return a result.
    /// Implements <see cref="ICommandHandler{TCommand, Unit}"/> to allow zero-reflection invocation.
    /// </summary>
    /// <typeparam name="TCommand">The type of command that this handler processes. Must implement <see cref="ICommand"/>.</typeparam>
    public interface ICommandHandler<in TCommand> : ICommandHandler<TCommand, Unit> where TCommand : ICommand
    {
        /// <summary>
        /// Asynchronously handles the given command.
        /// </summary>
        /// <param name="command">The command instance to be processed.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        new Task HandleAsync(TCommand command, CancellationToken cancellationToken = default);

        /// <inheritdoc />
        Task<Unit> ICommandHandler<TCommand, Unit>.HandleAsync(TCommand command, CancellationToken cancellationToken)
        {
            return HandleCoreAsync(command, cancellationToken);

            async Task<Unit> HandleCoreAsync(TCommand cmd, CancellationToken ct)
            {
                await HandleAsync(cmd, ct);
                return Unit.Value;
            }
        }
    }
}
