namespace LlmProxy.Tests;

using LlmProxy.Application;
using LlmProxy.Application.Logging;
using LlmProxy.Application.Services;
using LlmProxy.Domain;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text.Json;

[Collection("LogDirectoryTests")]
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

    [Fact]
    public async Task LogTokenCountAsync_ConcurrentWrites_MaintainsIntegrityWithoutCorruptedLines()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var original = AppPaths.LogDirectory;
        AppPaths.LogDirectory = tempDir;

        try
        {
            var loggerMock = Substitute.For<ILogger<FileTokenCountLogger>>();
            var logger = new FileTokenCountLogger(loggerMock);

            var testDate = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
            const int count = 50;

            var tasks = Enumerable.Range(1, count).Select(async i =>
            {
                var log = new TokenCountLog(
                    Date: testDate,
                    Provider: $"provider-{i % 5}",
                    Model: $"model-{i}",
                    PromptTokens: i * 100,
                    CompletionTokens: i * 10,
                    TotalTokens: i * 110,
                    CachedTokens: i * 5,
                    RequestCount: 1,
                    ElapsedMs: i * 20);

                await logger.LogTokenCountAsync(log, CancellationToken.None);
            });

            await Task.WhenAll(tasks);

            var logFileName = $"tokens-{testDate:yyyy-MM-dd}.jsonl";
            var logFilePath = Path.Combine(tempDir, logFileName);

            Assert.True(File.Exists(logFilePath));

            var lines = await File.ReadAllLinesAsync(logFilePath, CancellationToken.None);
            Assert.Equal(count, lines.Length);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var parsedEntries = lines
                .Select(line => JsonSerializer.Deserialize<FileUsageReaderLogEntry>(line, options))
                .ToList();

            Assert.All(parsedEntries, Assert.NotNull);
            var expectedTotalTokens = Enumerable.Range(1, count).Sum(i => i * 110);
            Assert.Equal(expectedTotalTokens, parsedEntries.Sum(e => e!.TotalTokens));
        }
        finally
        {
            AppPaths.LogDirectory = original;
            Directory.Delete(tempDir, true);
        }
    }
}
