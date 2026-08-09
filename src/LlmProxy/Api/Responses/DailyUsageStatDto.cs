namespace LlmProxy.Api.Responses;

public class DailyUsageStatDto
{
    public DateOnly Date { get; set; }
    public int RequestCount { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public int CachedTokens { get; set; }
}
