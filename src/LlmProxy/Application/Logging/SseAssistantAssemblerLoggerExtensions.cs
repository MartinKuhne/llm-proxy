namespace LlmProxy.Application.Logging;

using Microsoft.Extensions.Logging;

/// <summary>
/// Semantic logging events for the SSE assistant message assembler (event ids 200+).
/// </summary>
public static class SseAssistantAssemblerLoggerExtensions
{
    private static readonly Action<ILogger, string, Exception?> _sseChunkSkipped =
        LoggerMessage.Define<string>(LogLevel.Debug, 200, "Skipped SSE chunk: {Reason}");

    public static void SseChunkSkipped(this ILogger logger, string reason, Exception? exception = null)
        => _sseChunkSkipped(logger, reason, exception);
}
