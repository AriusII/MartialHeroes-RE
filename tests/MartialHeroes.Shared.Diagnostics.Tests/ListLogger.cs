using Microsoft.Extensions.Logging;

namespace MartialHeroes.Shared.Diagnostics.Tests;

/// <summary>
/// A minimal in-memory <see cref="ILogger"/> that captures every emitted record so tests can
/// assert the <see cref="EventId"/>, <see cref="LogLevel"/>, and the rendered message template
/// produced by the <c>[LoggerMessage]</c> source generator.
/// </summary>
internal sealed class ListLogger : ILogger
{
    public readonly List<LogRecord> Records = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Records.Add(new LogRecord(logLevel, eventId, formatter(state, exception)));
    }
}

internal readonly record struct LogRecord(LogLevel Level, EventId EventId, string Message);
