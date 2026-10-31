namespace LlmProxy.Application;

/// <summary>
/// Shared application paths used across multiple services.
/// </summary>
public static class AppPaths
{
    private static string _logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

    private static string _conversationDirectory = Path.Combine(AppContext.BaseDirectory, "logs", "conversations");

    /// <summary>
    /// Directory for JSONL token-count log files.
    /// </summary>
    public static string LogDirectory
    {
        get => _logDirectory;
        set => _logDirectory = value;
    }

    /// <summary>
    /// Directory for the conversation audit log: one JSONL file per conversation plus an <c>index.jsonl</c> summary file.
    /// </summary>
    public static string ConversationDirectory
    {
        get => _conversationDirectory;
        set => _conversationDirectory = value;
    }
}
