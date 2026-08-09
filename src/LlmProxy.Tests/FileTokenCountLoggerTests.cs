namespace LlmProxy.Tests;

using LlmProxy.Application.Logging;
using LlmProxy.Application.Services;
using LlmProxy.Domain;
using Microsoft.Extensions.Logging;
using NSubstitute;

public class FileTokenCountLoggerTests
{
    private readonly FileTokenCountLogger _logger;

    public FileTokenCountLoggerTests()
    {
        var logger = Substitute.For<ILogger<FileTokenCountLogger>>();
        _logger = new FileTokenCountLogger(logger);
    }

    [Fact]
    public async Task LogTokenCountAsync_WritesToFile()
    {
        var log = new TokenCountLog(
            Date: DateTime.UtcNow,
            Provider: "provider1",
            Model: "test-model",
            PromptTokens: 10,
            CompletionTokens: 20,
            TotalTokens: 30,
            CachedTokens: 0,
            RequestCount: 1,
            ElapsedMs: 1000);

        await _logger.LogTokenCountAsync(log, CancellationToken.None);

        var expectedFileName = $"tokens-{log.Date:yyyy-MM-dd}.jsonl";
        var expectedFilePath = Path.Combine(Path.Combine(AppContext.BaseDirectory, "logs"), expectedFileName);

        Assert.True(File.Exists(expectedFilePath));

        var lines = await File.ReadAllLinesAsync(expectedFilePath, CancellationToken.None);
        Assert.Contains(lines, l => l.Contains("\"TotalTokens\":30"));
    }
}
