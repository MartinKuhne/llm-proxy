namespace LlmProxy.Api.Responses;

public class ProviderUsageStatDto
{
    public string ProviderName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int RequestCount { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public int CachedTokens { get; set; }
    public double AvgPromptTokensPerSec { get; set; }
    public double AvgCompletionTokensPerSec { get; set; }
}
