namespace LlmProxy.Application.Services;

using System.Text.Json;
using LlmProxy.Application.Logging;
using LlmProxy.Domain;

/// <summary>
/// Reads the conversation audit log from disk: listings from the in-memory index, single conversations by streaming
/// their JSONL file line by line.
/// </summary>
public sealed class FileConversationReader : IConversationReader
{
    private static readonly JsonSerializerOptions _readOptions =
        new() { PropertyNameCaseInsensitive = true };

    private readonly ILogger<FileConversationReader> _logger;
    private readonly ConversationIndex _index;

    public FileConversationReader(ILogger<FileConversationReader> logger, ConversationIndex index)
    {
        _logger = logger;
        _index = index;
    }

    /// <inheritdoc />
    public async Task<ConversationPage> ListAsync(ConversationQuery query, CancellationToken cancellationToken = default)
    {
        await _index.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        // The index snapshot is newest-first and filtering keeps that order.
        var matches = _index.All().Where(summary => Matches(summary, query)).ToList();

        var totalCount = matches.Count;
        var start = (long)(query.Page - 1) * query.PageSize;
        var items = start >= totalCount ? [] : matches.GetRange((int)start, Math.Min(query.PageSize, totalCount - (int)start));

        return new ConversationPage(items, query.Page, query.PageSize, totalCount, TotalPages(totalCount, query.PageSize));
    }

    /// <inheritdoc />
    /// <remarks>
    /// The index is not force-loaded here: when it holds no entry for the conversation the summary is derived from the
    /// events on disk, which is also what a reader pointed at a log directory that was never written to sees.
    /// </remarks>
    public async Task<ConversationDetail?> GetAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var path = ConversationFileNames.PathFor(conversationId);
        if (!File.Exists(path))
        {
            return null;
        }

        var fileName = Path.GetFileName(path);
        var events = await ReadEventsAsync(path, cancellationToken).ConfigureAwait(false);
        var summary = _index.TryGetSummary(conversationId, out var indexed) && indexed is not null
            ? indexed
            : ConversationSummarizer.Summarize(conversationId, events);

        _logger.ConversationRead(fileName, events.Count);

        return new ConversationDetail(
            conversationId,
            summary.Title,
            summary.StartedAt,
            summary.LastActivityAt,
            summary.TurnCount,
            summary.EventCount,
            summary.Provider,
            summary.Model,
            summary.TotalTokens,
            summary.HasErrors,
            events);
    }

    private static int TotalPages(int totalCount, int pageSize) => (totalCount + pageSize - 1) / pageSize;

    private static bool Matches(ConversationSummary summary, ConversationQuery query)
    {
        var activityDate = DateOnly.FromDateTime(summary.LastActivityAt.UtcDateTime);
        if (query.From is { } from && activityDate < from)
        {
            return false;
        }

        if (query.To is { } to && activityDate > to)
        {
            return false;
        }

        if (query.Provider is { } provider &&
            !string.Equals(summary.Provider, provider, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return query.Search is not { } search ||
               Contains(summary.Title, search) ||
               Contains(summary.Id, search) ||
               Contains(summary.Provider, search) ||
               Contains(summary.Model, search);
    }

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private async Task<IReadOnlyList<ConversationEvent>> ReadEventsAsync(string path, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(path);
        var events = new List<ConversationEvent>();
        var lineNumber = 0;

        await foreach (var line in File.ReadLinesAsync(path, cancellationToken).ConfigureAwait(false))
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
                _logger.ConversationReadLineSkipped(fileName, lineNumber);
                continue;
            }

            if (evt is null)
            {
                _logger.ConversationReadLineSkipped(fileName, lineNumber);
                continue;
            }

            events.Add(evt);
        }

        return events;
    }
}
