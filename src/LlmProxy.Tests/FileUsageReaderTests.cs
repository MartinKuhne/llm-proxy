namespace LlmProxy.Tests;

using LlmProxy.Application;
using LlmProxy.Application.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

[Collection("LogDirectoryTests")]
public class FileUsageReaderTests
{
    [Fact]
    public async Task ReadTodayAsync_WithNoLogFile_ReturnsEmptyList()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var original = AppPaths.LogDirectory;
        AppPaths.LogDirectory = tempDir;

        try
        {
            var reader = new FileUsageReader();
            var result = await reader.ReadTodayAsync(CancellationToken.None);

            Assert.Empty(result);
        }
        finally
        {
            AppPaths.LogDirectory = original;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ReadTodayAsync_WithCorruptedAndInvalidLines_SkipsCorruptedLinesAndReturnsValidStats()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var original = AppPaths.LogDirectory;
        AppPaths.LogDirectory = tempDir;

        try
        {
            var entryDateUtc = DateTime.UtcNow;
            var fileName = $"tokens-{entryDateUtc:yyyy-MM-dd}.jsonl";
            var filePath = Path.Combine(tempDir, fileName);

            var validDateString = entryDateUtc.ToString("O");
            var content = string.Join(Environment.NewLine, new[]
            {
                "}",
                $"{{\"Date\":\"{validDateString}\",\"Provider\":\"TestProvider\",\"Model\":\"test-model\",\"PromptTokens\":100,\"CompletionTokens\":50,\"TotalTokens\":150,\"CachedTokens\":0,\"RequestCount\":1,\"ElapsedMs\":1000}}",
                "{ incomplete json",
                "   ",
                "{\"unexpected\":\"format\"}"
            });

            await File.WriteAllTextAsync(filePath, content);

            var logger = Substitute.For<ILogger<FileUsageReader>>();
            var reader = new FileUsageReader(logger);

            var result = await reader.ReadTodayAsync(CancellationToken.None);

            Assert.Single(result);
            Assert.Equal("TestProvider", result[0].Provider);
            Assert.Equal("test-model", result[0].Model);
            Assert.Equal(150, result[0].TotalTokens);
        }
        finally
        {
            AppPaths.LogDirectory = original;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ReadLast30DaysAsync_WithCorruptedAndInvalidLines_SkipsCorruptedLinesAndReturnsValidStats()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var original = AppPaths.LogDirectory;
        AppPaths.LogDirectory = tempDir;

        try
        {
            var entryDateUtc = DateTime.UtcNow;
            var fileName = $"tokens-{entryDateUtc:yyyy-MM-dd}.jsonl";
            var filePath = Path.Combine(tempDir, fileName);

            var validDateString = entryDateUtc.ToString("O");
            var content = string.Join(Environment.NewLine, new[]
            {
                "}",
                $"{{\"Date\":\"{validDateString}\",\"Provider\":\"TestProvider\",\"Model\":\"test-model\",\"PromptTokens\":200,\"CompletionTokens\":100,\"TotalTokens\":300,\"CachedTokens\":0,\"RequestCount\":1,\"ElapsedMs\":500}}",
                "{ not valid json }"
            });

            await File.WriteAllTextAsync(filePath, content);

            var logger = Substitute.For<ILogger<FileUsageReader>>();
            var reader = new FileUsageReader(logger);

            var result = await reader.ReadLast30DaysAsync(CancellationToken.None);

            Assert.Single(result);
            Assert.Equal(300, result[0].TotalTokens);
            Assert.Equal(1, result[0].RequestCount);
        }
        finally
        {
            AppPaths.LogDirectory = original;
            Directory.Delete(tempDir, true);
        }
    }
}
