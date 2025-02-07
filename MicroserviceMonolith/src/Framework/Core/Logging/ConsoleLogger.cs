using System;

namespace MicroserviceMonolith.Core.Logging
{
    public class ConsoleLogger : ILogger
    {
        public void Log(string message) => Console.WriteLine(message);
    }
}