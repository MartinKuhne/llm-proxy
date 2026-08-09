namespace LlmProxy.Application.Services;

using System.Text.Json;

public class FileUsageReaderLogEntry
{
    public DateTime Date { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public int CachedTokens { get; set; }
    public int RequestCount { get; set; }
    public long ElapsedMs { get; set; }
}
