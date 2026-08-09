namespace LlmProxy.Application.Services;

using LlmProxy.Application.Logging;
using LlmProxy.Domain;
using System.Text.Json;

public class FileTokenCountLogger : ITokenCountLogger
{
    private readonly ILogger<FileTokenCountLogger> _logger;

    public FileTokenCountLogger(ILogger<FileTokenCountLogger> logger)
    {
        _logger = logger;
        Directory.CreateDirectory(AppPaths.LogDirectory);
    }

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
        
        await File.AppendAllLinesAsync(logFilePath, new[] { jsonLine }, cancellationToken);
        
        _logger.LoggedTokenCount(logFileName, log.TotalTokens);
    }
}
