using System;

namespace MicroserviceMonolith.Core.Logging
{
    /// <summary>
    /// A simple logger implementation that writes log messages to the console.
    /// </summary>
    public class ConsoleLogger : ILogger
    {
        /// <summary>
        /// Logs a message to the console.
        /// </summary>
        /// <param name="message">The message to be logged.</param>
        public void Log(string message) => Console.WriteLine(message);  // Output the message to the console.
    }
}
