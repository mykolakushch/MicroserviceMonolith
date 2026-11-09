using System;

namespace MicroserviceMonolith.Core.Logging
{
    /// <summary>
    /// Thread-safe console logger writing formatted log entries to standard output.
    /// </summary>
    public class ConsoleLogger : ILogger
    {
        private readonly object _consoleLock = new();

        /// <inheritdoc />
        public void Log(string message)
        {
            lock (_consoleLock)
            {
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}");
            }
        }
    }
}
