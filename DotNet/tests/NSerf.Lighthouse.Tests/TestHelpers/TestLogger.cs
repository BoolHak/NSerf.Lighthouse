using Microsoft.Extensions.Logging;

namespace NSerf.Lighthouse.Tests.TestHelpers;

/// <summary>
/// Simple test logger that does nothing
/// </summary>
public class TestLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => false;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // Do nothing in tests
    }
}
