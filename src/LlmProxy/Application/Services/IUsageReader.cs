namespace LlmProxy.Application.Services;

public interface IUsageReader
{
    Task<IReadOnlyList<ProviderUsageStat>> ReadTodayAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailyUsageStat>> ReadLast30DaysAsync(CancellationToken cancellationToken = default);
}

public class DailyUsageStat
{
    public DateOnly Date { get; set; }
    public int RequestCount { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public int CachedTokens { get; set; }
}


