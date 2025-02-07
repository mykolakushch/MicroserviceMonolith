using System;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// Defines the contract for a command middleware that processes commands in the command pipeline.
    /// </summary>
    /// <typeparam name="TCommand">The type of the command being processed. Must implement <see cref="ICommand"/>.</typeparam>
    public interface ICommandMiddleware
    {
        /// <summary>
        /// Asynchronously processes the given command and invokes the next step in the command pipeline.
        /// </summary>
        /// <param name="command">The command instance to be processed by this middleware.</param>
        /// <param name="next">A delegate representing the next step in the command pipeline to be invoked after this middleware completes its work.</param>
        /// <returns>A task representing the asynchronous operation of this middleware.</returns>
        Task InvokeAsync<TCommand>(TCommand command, Func<Task> next) where TCommand : ICommand;
    }
}
