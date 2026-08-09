namespace LlmProxy.Tests;

using LlmProxy.Application;
using LlmProxy.Application.Services;

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
}
