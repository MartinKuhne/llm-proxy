namespace LlmProxy.Application.Logging;

using Microsoft.Extensions.Logging;

/// <summary>
/// Provides logging extension methods for <see cref="Services.FileUsageReader"/>.
/// </summary>
public static class FileUsageReaderLoggerExtensions
{
    private static readonly Action<ILogger, string, string, Exception?> _invalidJsonLineSkipped =
        LoggerMessage.Define<string, string>(LogLevel.Warning, 20, "Skipped invalid JSON line in {FilePath}: {Line}");

    /// <summary>
    /// Logs a warning that an invalid JSON line was skipped while reading usage data.
    /// </summary>
    public static void InvalidJsonLineSkipped(this ILogger logger, string filePath, string line, Exception? exception = null)
    {
        _invalidJsonLineSkipped(logger, filePath, line, exception);
    }
}
