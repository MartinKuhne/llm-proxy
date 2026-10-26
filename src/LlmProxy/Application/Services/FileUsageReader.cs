namespace LlmProxy.Application.Services;

using LlmProxy.Application.Logging;
using Microsoft.Extensions.Logging;
using System.Text.Json;

/// <summary>
/// Reads token usage statistics from daily JSONL log files.
/// </summary>
public class FileUsageReader : IUsageReader
{
    private static readonly JsonSerializerOptions _jsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private readonly ILogger<FileUsageReader>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileUsageReader"/> class.
    /// </summary>
    public FileUsageReader(ILogger<FileUsageReader>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Reads token usage statistics grouped by provider and model for today.
    /// </summary>
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

                if (!TryParseEntry(filePath, line, out var entry) || entry == null)
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

    /// <summary>
    /// Reads daily token usage statistics for the last 30 days.
    /// </summary>
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

                if (!TryParseEntry(filePath, line, out var entry) || entry == null)
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

    private bool TryParseEntry(string filePath, string line, out FileUsageReaderLogEntry? entry)
    {
        try
        {
            entry = JsonSerializer.Deserialize<FileUsageReaderLogEntry>(line, _jsonOptions);
            return entry != null;
        }
        catch (JsonException ex)
        {
            _logger?.InvalidJsonLineSkipped(filePath, line, ex);
            entry = null;
            return false;
        }
    }
}
