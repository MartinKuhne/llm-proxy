namespace LlmProxy.Application.Services;

using System.Text.Json;

public class FileUsageReader : IUsageReader
{
    private static readonly JsonSerializerOptions _jsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<ProviderUsageStat>> ReadTodayAsync(CancellationToken cancellationToken = default)
    {
        var utc = DateTime.UtcNow;
        var localNow = DateTime.Now;

        var startTime = localNow.Date;
        var endTime = startTime.AddDays(1);

        var startTimeUtc = startTime.ToUniversalTime();
        var endTimeUtc = endTime.ToUniversalTime();

        var fileNames = new[]
        {
            $"tokens-{utc.AddDays(-1):yyyy-MM-dd}.jsonl",
            $"tokens-{utc:yyyy-MM-dd}.jsonl",
            $"tokens-{utc.AddDays(1):yyyy-MM-dd}.jsonl"
        };

        var stats = new Dictionary<string, ProviderUsageStat>(StringComparer.OrdinalIgnoreCase);

        foreach (var fileName in fileNames)
        {
            var filePath = Path.Combine(AppPaths.LogDirectory, fileName);
            if (!File.Exists(filePath))
                continue;

            var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var entry = JsonSerializer.Deserialize<FileUsageReaderLogEntry>(line, _jsonOptions);
                if (entry == null)
                    continue;

                var entryTime = entry.Date;
                if (entryTime < startTimeUtc || entryTime >= endTimeUtc)
                    continue;

            var key = $"{entry.Provider}|{entry.Model}";
                 if (!stats.TryGetValue(key, out var stat))
                 {
                     stat = new ProviderUsageStat { Provider = entry.Provider, Model = entry.Model };
                    stats[key] = stat;
                }

                stat.RequestCount += entry.RequestCount;
                stat.PromptTokens += entry.PromptTokens;
                stat.CompletionTokens += entry.CompletionTokens;
                stat.TotalTokens += entry.TotalTokens;
                stat.CachedTokens += entry.CachedTokens;
                stat.TotalElapsedMs += entry.ElapsedMs;
            }
        }

        return [.. stats.Values];
    }

    public async Task<IReadOnlyList<DailyUsageStat>> ReadLast30DaysAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;
        var startDate = utcNow.AddDays(-29).Date;
        var endDate = utcNow.Date.AddDays(1);

        var stats = new Dictionary<string, DailyUsageStat>();

        for (var date = startDate; date < endDate; date = date.AddDays(1))
        {
            var fileName = $"tokens-{date:yyyy-MM-dd}.jsonl";
            var filePath = Path.Combine(AppPaths.LogDirectory, fileName);
            
            if (!File.Exists(filePath))
                continue;

            var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var entry = JsonSerializer.Deserialize<FileUsageReaderLogEntry>(line, _jsonOptions);
                if (entry == null)
                    continue;

                var dateKey = DateOnly.FromDateTime(entry.Date.Date);
                if (!stats.TryGetValue(dateKey.ToString("yyyy-MM-dd"), out var stat))
                {
                    stat = new DailyUsageStat { Date = dateKey };
                    stats[dateKey.ToString("yyyy-MM-dd")] = stat;
                }

                stat.RequestCount += entry.RequestCount;
                stat.PromptTokens += entry.PromptTokens;
                stat.CompletionTokens += entry.CompletionTokens;
                stat.TotalTokens += entry.TotalTokens;
                stat.CachedTokens += entry.CachedTokens;
            }
        }

        var result = stats.Values.ToList();
        result.Sort((a, b) => a.Date.CompareTo(b.Date));
        return result;
    }
}
