using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Modules
{
    public interface IModule
    {
        Task InitializeAsync();
        Task ShutdownAsync();
    }
}
