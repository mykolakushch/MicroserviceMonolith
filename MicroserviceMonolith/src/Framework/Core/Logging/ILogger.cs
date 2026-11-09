using System;

namespace MicroserviceMonolith.Core.Logging
{
    /// <summary>
    /// Simple application logging contract for outputting diagnostic messages.
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// Logs an informational message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        void Log(string message);
    }
}
