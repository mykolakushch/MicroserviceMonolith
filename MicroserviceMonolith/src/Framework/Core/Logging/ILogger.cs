using System;

namespace MicroserviceMonolith.Core.Logging
{
    /// <summary>
    /// Severity of a framework log entry.
    /// </summary>
    public enum LogSeverity
    {
        Debug = 0,
        Information = 1,
        Warning = 2,
        Error = 3
    }

    /// <summary>
    /// Minimal logging abstraction used by the framework.
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// Writes an informational message.
        /// </summary>
        void Log(string message);

        /// <summary>
        /// Writes a message with a severity. The framework uses <see cref="LogSeverity.Error"/> and
        /// <see cref="LogSeverity.Warning"/> for failures and slow operations. The default implementation ignores
        /// the severity and calls <see cref="Log(string)"/>, so existing implementations keep working; override it to
        /// route messages to a real log level.
        /// </summary>
        void Log(LogSeverity severity, string message) => Log(message);
    }
}
