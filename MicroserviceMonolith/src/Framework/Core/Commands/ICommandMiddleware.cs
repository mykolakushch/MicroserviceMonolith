using System;
using System.Threading;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// Defines the contract for a command middleware that intercepts commands in the command processing pipeline.
    /// </summary>
    public interface ICommandMiddleware
    {
        /// <summary>
        /// Asynchronously intercepts and processes the given command and invokes the next step in the pipeline.
        /// </summary>
        /// <typeparam name="TCommand">The command type being processed.</typeparam>
        /// <typeparam name="TResult">The expected result type.</typeparam>
        /// <param name="command">The command instance.</param>
        /// <param name="next">Delegate to invoke the next middleware or final handler.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task representing the result of the command execution.</returns>
        Task<TResult> InvokeAsync<TCommand, TResult>(
            TCommand command,
            Func<Task<TResult>> next,
            CancellationToken cancellationToken = default)
            where TCommand : ICommand<TResult>;
    }
}
