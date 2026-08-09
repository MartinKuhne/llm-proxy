namespace LlmProxy.Tests;

using LlmProxy.Application.Logging;
using Microsoft.Extensions.Logging;

public class ProxyServiceLoggerExtensionsTests
{
    private readonly List<string> _logs;

    public ProxyServiceLoggerExtensionsTests()
    {
        _logs = new List<string>();
    }

    private ILogger CreateLogger()
    {
        return new TestLogger(message => _logs.Add(message));
    }

    [Fact]
    public void ProxyError_WritesErrorLog()
    {
        var logger = CreateLogger();
        logger.ProxyError("http://test.com", "test error", null);

        Assert.Contains(_logs, l => l.Contains("Proxy error for provider"));
        Assert.Contains(_logs, l => l.Contains("http://test.com"));
        Assert.Contains(_logs, l => l.Contains("test error"));
    }

    [Fact]
    public void ProxyUpstreamError_WritesWarningLog()
    {
        var logger = CreateLogger();
        logger.ProxyUpstreamError("http://test.com", 404);

        Assert.Contains(_logs, l => l.Contains("Provider"));
        Assert.Contains(_logs, l => l.Contains("http://test.com"));
        Assert.Contains(_logs, l => l.Contains("404"));
    }

    [Fact]
    public void ProxyNoProviders_WritesWarningLog()
    {
        var logger = CreateLogger();
        logger.ProxyNoProviders();

        Assert.Contains(_logs, l => l.Contains("No providers available"));
    }

    [Fact]
    public void ProxyUsage_WritesInfoLog()
    {
        var logger = CreateLogger();
        logger.ProxyUsage("http://test.com", 1000, 10.5, 20.3);

        Assert.Contains(_logs, l => l.Contains("Provider"));
        Assert.Contains(_logs, l => l.Contains("http://test.com"));
        Assert.Contains(_logs, l => l.Contains("1000ms"));
    }

    [Fact]
    public void ProxyParsingUsage_WritesDebugLog()
    {
        var logger = CreateLogger();
        logger.ProxyParsingUsage("http://test.com");

        Assert.Contains(_logs, l => l.Contains("Parsing usage"));
        Assert.Contains(_logs, l => l.Contains("http://test.com"));
    }

    [Fact]
    public void ProxyNoUsageData_WritesWarningLog()
    {
        var logger = CreateLogger();
        logger.ProxyNoUsageData("http://test.com");

        Assert.Contains(_logs, l => l.Contains("No usage data found"));
        Assert.Contains(_logs, l => l.Contains("http://test.com"));
    }

    [Fact]
    public void ProxySsePacket_WritesDebugLog()
    {
        var logger = CreateLogger();
        logger.ProxySsePacket("http://test.com", 1024);

        Assert.Contains(_logs, l => l.Contains("SSE packet"));
        Assert.Contains(_logs, l => l.Contains("http://test.com"));
        Assert.Contains(_logs, l => l.Contains("1024 bytes"));
    }

    [Fact]
    public void ProxySseProgress_WritesInfoLog()
    {
        var logger = CreateLogger();
        logger.ProxySseProgress("http://test.com", 5);

        Assert.Contains(_logs, l => l.Contains("SSE stream"));
        Assert.Contains(_logs, l => l.Contains("http://test.com"));
        Assert.Contains(_logs, l => l.Contains("5 packets"));
    }

    [Fact]
    public void ProxyUpstreamErrorWithBody_WritesWarningLog()
    {
        var logger = CreateLogger();
        logger.ProxyUpstreamErrorWithBody("http://test.com", 500, "Internal Server Error");

        Assert.Contains(_logs, l => l.Contains("Provider"));
        Assert.Contains(_logs, l => l.Contains("http://test.com"));
        Assert.Contains(_logs, l => l.Contains("500"));
        Assert.Contains(_logs, l => l.Contains("Internal Server Error"));
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
