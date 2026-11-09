using System;
using Microsoft.Extensions.Logging;

namespace MicroserviceMonolith.Core.Logging
{
    /// <summary>
    /// Adapts a <see cref="Microsoft.Extensions.Logging.ILogger"/> to the framework's <see cref="ILogger"/> interface.
    /// </summary>
    public class MicrosoftLoggingAdapter : ILogger
    {
        private readonly Microsoft.Extensions.Logging.ILogger _logger;

        public MicrosoftLoggingAdapter(Microsoft.Extensions.Logging.ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public void Log(string message)
        {
            _logger.LogInformation("{Message}", message);
        }
    }
}
