using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Logging
{
    public interface ILogger
    {
        void Log(string message);
    }
}
