namespace LlmProxy.Application.Services;

 public class ProviderUsageStat
{
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int RequestCount { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public int CachedTokens { get; set; }
    public long TotalElapsedMs { get; set; }
}
