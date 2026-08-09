namespace LlmProxy.Application.Logging;

using Microsoft.Extensions.Logging;

public static class FileTokenCountLoggerLoggerExtensions
{
    private static readonly Action<ILogger, string, int, Exception?> _loggedTokenCount =
        LoggerMessage.Define<string, int>(LogLevel.Information, 4, "Logged token count to {FileName} (Total: {TotalTokens} tokens)");

    public static void LoggedTokenCount(this ILogger logger, string fileName, int totalTokens)
    {
        _loggedTokenCount(logger, fileName, totalTokens, null);
    }
}
