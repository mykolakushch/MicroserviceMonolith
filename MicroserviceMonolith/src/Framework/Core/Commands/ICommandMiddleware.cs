using System;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Commands
{
    public interface ICommandMiddleware
    {
        Task InvokeAsync<TCommand>(TCommand command, Func<Task> next) where TCommand : ICommand;
    }
}
