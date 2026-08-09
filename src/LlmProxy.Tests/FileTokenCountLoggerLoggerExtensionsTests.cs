namespace LlmProxy.Tests;

using LlmProxy.Application.Logging;
using Microsoft.Extensions.Logging;

public class FileTokenCountLoggerLoggerExtensionsTests
{
    private readonly List<string> _logs;

    public FileTokenCountLoggerLoggerExtensionsTests()
    {
        _logs = new List<string>();
    }

    private ILogger CreateLogger()
    {
        return new TestLogger(message => _logs.Add(message));
    }

    [Fact]
    public void LoggedTokenCount_WritesInfoLog()
    {
        var logger = CreateLogger();
        logger.LoggedTokenCount("tokens-2024-01-01.jsonl", 100);

        Assert.Contains(_logs, l => l.Contains("Logged token count to"));
        Assert.Contains(_logs, l => l.Contains("tokens-2024-01-01.jsonl"));
        Assert.Contains(_logs, l => l.Contains("100 tokens"));
    }

    private class TestLogger : ILogger
    {
        private readonly Action<string> _write;

        public TestLogger(Action<string> write)
        {
            _write = write;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => throw new NotImplementedException();

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _write(formatter(state, exception));
        }
    }
}
