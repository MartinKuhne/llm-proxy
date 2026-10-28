namespace LlmProxy.Application.Logging;

using Microsoft.Extensions.Logging;

/// <summary>
/// Semantic logging events for the conversation audit log (event ids 100+).
/// </summary>
public static class ConversationLoggerLoggerExtensions
{
    private static readonly Action<ILogger, string, int, Exception?> _conversationWritten =
        LoggerMessage.Define<string, int>(LogLevel.Debug, 100, "Wrote {EventCount} conversation events to {FileName}");

    private static readonly Action<ILogger, int, Exception?> _conversationIndexRebuilt =
        LoggerMessage.Define<int>(LogLevel.Information, 101, "Rebuilt conversation index from {FileCount} conversation files");

    private static readonly Action<ILogger, string, int, Exception?> _conversationIndexLineSkipped =
        LoggerMessage.Define<string, int>(LogLevel.Warning, 102, "Skipped invalid line {LineNumber} of conversation file {FileName}");

    private static readonly Action<ILogger, string, Exception?> _conversationLoggingFailed =
        LoggerMessage.Define<string>(LogLevel.Error, 103, "Conversation logging failed for conversation {ConversationId}");

    private static readonly Action<ILogger, Exception?> _conversationLoggingDisabled =
        LoggerMessage.Define(LogLevel.Debug, 104, "Conversation audit logging is disabled");

    private static readonly Action<ILogger, string, int, Exception?> _conversationReadLineSkipped =
        LoggerMessage.Define<string, int>(LogLevel.Warning, 105, "Skipped unreadable line {LineNumber} of conversation file {FileName} while reading it back");

    private static readonly Action<ILogger, string, int, Exception?> _conversationRead =
        LoggerMessage.Define<string, int>(LogLevel.Debug, 106, "Read {EventCount} events of conversation file {FileName}");

    public static void ConversationWritten(this ILogger logger, string fileName, int eventCount)
        => _conversationWritten(logger, fileName, eventCount, null);

    public static void ConversationIndexRebuilt(this ILogger logger, int fileCount)
        => _conversationIndexRebuilt(logger, fileCount, null);

    public static void ConversationIndexLineSkipped(this ILogger logger, string fileName, int lineNumber)
        => _conversationIndexLineSkipped(logger, fileName, lineNumber, null);

    public static void ConversationLoggingFailed(this ILogger logger, string conversationId, Exception? exception = null)
        => _conversationLoggingFailed(logger, conversationId, exception);

    public static void ConversationLoggingDisabled(this ILogger logger)
        => _conversationLoggingDisabled(logger, null);

    public static void ConversationReadLineSkipped(this ILogger logger, string fileName, int lineNumber)
        => _conversationReadLineSkipped(logger, fileName, lineNumber, null);

    public static void ConversationRead(this ILogger logger, string fileName, int eventCount)
        => _conversationRead(logger, fileName, eventCount, null);
}
