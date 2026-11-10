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
            Log(LogSeverity.Information, message);
        }

        /// <inheritdoc />
        public void Log(LogSeverity severity, string message)
        {
            var tag = severity switch
            {
                LogSeverity.Debug => "DBG ",
                LogSeverity.Warning => "WARN ",
                LogSeverity.Error => "ERR ",
                _ => string.Empty
            };

            lock (_consoleLock)
            {
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {tag}{message}");
            }
        }
    }
}
