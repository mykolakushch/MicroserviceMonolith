using MicroserviceMonolith.Core.Common;

namespace MicroserviceMonolith.Core.Commands
{
    /// <summary>
    /// Represents a command that returns a result of type <typeparamref name="TResult"/>.
    /// </summary>
    /// <typeparam name="TResult">The type of result returned after processing the command.</typeparam>
    public interface ICommand<out TResult> { }

    /// <summary>
    /// Represents a command that does not return a result.
    /// Commands encapsulate a request to perform a specific action or mutation.
    /// </summary>
    public interface ICommand : ICommand<Unit> { }
}
