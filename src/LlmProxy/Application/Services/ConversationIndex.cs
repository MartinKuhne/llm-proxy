namespace LlmProxy.Application.Services;

using System.Collections.Concurrent;
using System.Text.Json;
using LlmProxy.Application.Logging;
using LlmProxy.Domain;

/// <summary>
/// Thread-safe index of <see cref="ConversationSummary"/> entries keyed by conversation id, backed by an
/// append-only <c>index.jsonl</c> file so the roll-up survives a restart without rewriting the file.
/// </summary>
public sealed class ConversationIndex
{
    internal const string IndexFileName = "index.jsonl";

    private static readonly JsonSerializerOptions _readOptions =
        new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions _writeOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly SemaphoreSlim _writeLock = new(1, 1);

    private readonly ConcurrentDictionary<string, ConversationSummary> _summaries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly ILogger<ConversationIndex> _logger;

    private volatile bool _loaded;

    public ConversationIndex(ILogger<ConversationIndex> logger)
    {
        _logger = logger;
    }

    /// <summary>Returns the summary for a conversation, creating it on first use.</summary>
    /// <param name="conversationId">Conversation to look up.</param>
    /// <param name="timestamp">Activity timestamp to record.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<ConversationSummary> TouchAsync(string conversationId, DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Touch(conversationId, timestamp));
    }

    /// <summary>Returns the summary of one conversation without creating it.</summary>
    /// <param name="conversationId">Conversation to look up.</param>
    /// <param name="summary">The known summary, when there is one.</param>
    public bool TryGetSummary(string conversationId, out ConversationSummary? summary) =>
        _summaries.TryGetValue(conversationId, out summary);

    /// <summary>Folds a persisted event into the summary of its conversation.</summary>
    /// <param name="evt">Event to apply.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task RecordEventAsync(ConversationEvent evt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var summary = Touch(evt.ConversationId, evt.Timestamp);
        lock (summary)
        {
            ConversationSummarizer.Fold(summary, evt);
        }

        return Task.CompletedTask;
    }

    /// <summary>Snapshot of every known summary, most recently active first.</summary>
    public IReadOnlyList<ConversationSummary> All() =>
        [.. _summaries.Values.OrderByDescending(summary => summary.LastActivityAt)];

    /// <summary>
    /// Loads the index at most once: from <c>index.jsonl</c> when it exists, otherwise by rebuilding it from the
    /// conversation files in the directory.
    /// </summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return;
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            var indexPath = Path.Combine(AppPaths.ConversationDirectory, IndexFileName);
            if (File.Exists(indexPath))
            {
                await LoadIndexAsync(indexPath, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RebuildAsync(cancellationToken).ConfigureAwait(false);
            }

            _loaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>Appends the summary as one JSON line to <c>index.jsonl</c>; the last write wins on reload.</summary>
    /// <param name="summary">Summary to persist.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task PersistAsync(ConversationSummary summary, CancellationToken cancellationToken = default)
    {
        var directory = AppPaths.ConversationDirectory;
        var indexPath = Path.Combine(directory, IndexFileName);
        var line = JsonSerializer.Serialize(summary, _writeOptions);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);
            await File.AppendAllLinesAsync(indexPath, new[] { line }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Rebuilds the in-memory summaries by streaming every conversation file, skipping <c>index.jsonl</c>.</summary>
    /// <param name="cancellationToken">Cancels the rebuild.</param>
    internal async Task RebuildAsync(CancellationToken cancellationToken)
    {
        var directory = AppPaths.ConversationDirectory;
        var fileCount = 0;

        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl"))
            {
                if (string.Equals(Path.GetFileName(file), IndexFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                fileCount++;
                await FoldEventsAsync(file, cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.ConversationIndexRebuilt(fileCount);
    }

    private ConversationSummary Touch(string conversationId, DateTimeOffset timestamp)
    {
        var summary = _summaries.GetOrAdd(
            conversationId,
            id => new ConversationSummary { Id = id, StartedAt = timestamp, LastActivityAt = timestamp });

        lock (summary)
        {
            if (timestamp > summary.LastActivityAt)
            {
                summary.LastActivityAt = timestamp;
            }
        }

        return summary;
    }

    private async Task FoldEventsAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(filePath);
        var lineNumber = 0;

        await foreach (var line in File.ReadLinesAsync(filePath, cancellationToken).ConfigureAwait(false))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            ConversationEvent? evt;
            try
            {
                evt = JsonSerializer.Deserialize<ConversationEvent>(line, _readOptions);
            }
            catch (JsonException)
            {
                _logger.ConversationIndexLineSkipped(fileName, lineNumber);
                continue;
            }

            if (evt is null || string.IsNullOrEmpty(evt.ConversationId))
            {
                _logger.ConversationIndexLineSkipped(fileName, lineNumber);
                continue;
            }

            var summary = Touch(evt.ConversationId, evt.Timestamp);
            lock (summary)
            {
                ConversationSummarizer.Fold(summary, evt);
            }
        }
    }

    private async Task LoadIndexAsync(string indexPath, CancellationToken cancellationToken)
    {
        var lineNumber = 0;

        await foreach (var line in File.ReadLinesAsync(indexPath, cancellationToken).ConfigureAwait(false))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            ConversationSummary? summary;
            try
            {
                summary = JsonSerializer.Deserialize<ConversationSummary>(line, _readOptions);
            }
            catch (JsonException)
            {
                _logger.ConversationIndexLineSkipped(IndexFileName, lineNumber);
                continue;
            }

            if (summary is null || string.IsNullOrEmpty(summary.Id))
            {
                _logger.ConversationIndexLineSkipped(IndexFileName, lineNumber);
                continue;
            }

            _summaries[summary.Id] = summary;
        }
    }
}
