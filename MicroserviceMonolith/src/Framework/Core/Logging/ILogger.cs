using System;

namespace MicroserviceMonolith.Core.Logging
{
    /// <summary>
    /// Defines a contract for logging messages in the application.
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// Logs a message.
        /// </summary>
        /// <param name="message">The message to be logged.</param>
        void Log(string message);
    }
}
