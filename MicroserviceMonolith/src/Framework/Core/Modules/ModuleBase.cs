using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Modules
{
    public abstract class ModuleBase : IModule
    {
        public virtual Task InitializeAsync() => Task.CompletedTask;
        public virtual Task ShutdownAsync() => Task.CompletedTask;
    }
}
