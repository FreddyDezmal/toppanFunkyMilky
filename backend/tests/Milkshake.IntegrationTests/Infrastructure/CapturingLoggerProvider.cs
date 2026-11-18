using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Milkshake.IntegrationTests.Infrastructure;

/// <summary>Collects every log line (message, structured values and exceptions) so tests can search them.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            var line = new StringBuilder(formatter(state, exception));

            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach ((string key, object? value) in values)
                {
                    line.Append(' ').Append(key).Append('=').Append(value);
                }
            }

            if (exception is not null)
            {
                line.Append(' ').Append(exception);
            }

            messages.Enqueue(line.ToString());
        }
    }
}