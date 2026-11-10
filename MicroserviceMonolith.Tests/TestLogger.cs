using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace MicroserviceMonolith.Tests
{
    /// <summary>A <see cref="ILogger"/> that records every entry for assertions.</summary>
    public sealed class TestLogger : ILogger
    {
        public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

        private readonly ConcurrentQueue<Entry> _entries = new();

        public Entry[] Entries => _entries.ToArray();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _entries.Enqueue(new Entry(logLevel, formatter(state, exception), exception));
    }
}
