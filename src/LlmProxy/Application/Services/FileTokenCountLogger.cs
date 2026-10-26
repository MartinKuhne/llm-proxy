namespace LlmProxy.Application.Services;

using LlmProxy.Application.Logging;
using LlmProxy.Domain;
using System.Text.Json;

/// <summary>
/// Logs token usage statistics to daily JSONL files.
/// </summary>
public class FileTokenCountLogger : ITokenCountLogger
{
    private static readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ILogger<FileTokenCountLogger> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileTokenCountLogger"/> class.
    /// </summary>
    public FileTokenCountLogger(ILogger<FileTokenCountLogger> logger)
    {
        _logger = logger;
        Directory.CreateDirectory(AppPaths.LogDirectory);
    }

    /// <summary>
    /// Asynchronously logs token count statistics for a completed request.
    /// </summary>
    public async Task LogTokenCountAsync(TokenCountLog log, CancellationToken cancellationToken = default)
    {
        var logFileName = $"tokens-{log.Date:yyyy-MM-dd}.jsonl";
        var logFilePath = Path.Combine(AppPaths.LogDirectory, logFileName);

        var logEntry = new
        {
            log.Date,
            log.Provider,
            log.Model,
            log.PromptTokens,
            log.CompletionTokens,
            log.TotalTokens,
            log.CachedTokens,
            log.RequestCount,
            log.ElapsedMs
        };

        var jsonLine = JsonSerializer.Serialize(logEntry);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllLinesAsync(logFilePath, [jsonLine], cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }

        _logger.LoggedTokenCount(logFileName, log.TotalTokens);
    }
}
