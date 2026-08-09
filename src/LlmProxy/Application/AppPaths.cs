namespace LlmProxy.Application;

/// <summary>
/// Shared application paths used across multiple services.
/// </summary>
public static class AppPaths
{
    private static string _logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

    /// <summary>
    /// Directory for JSONL token-count log files.
    /// </summary>
    public static string LogDirectory
    {
        get => _logDirectory;
        set => _logDirectory = value;
    }
}
