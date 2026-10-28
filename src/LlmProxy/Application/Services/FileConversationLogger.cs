namespace LlmProxy.Application.Services;

using System.Text.Json;
using LlmProxy.Application.Logging;
using LlmProxy.Domain;

/// <summary>
/// Persists the conversation audit log as one JSONL file per conversation, keeping the conversation index current.
/// </summary>
/// <remarks>
/// Nothing thrown while deriving or writing events reaches the caller: auditing must never break a proxied request.
/// </remarks>
public class FileConversationLogger : IConversationLogger
{
    private static readonly JsonSerializerOptions _jsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly SemaphoreSlim _writeLock = new(1, 1);

    private readonly ILogger<FileConversationLogger> _logger;
    private readonly ConversationIndex _index;
    private readonly ConversationOptions _options;
    private readonly string _directory;

    public FileConversationLogger(ILogger<FileConversationLogger> logger, ConversationIndex index, ConversationOptions options)
    {
        _logger = logger;
        _index = index;
        _options = options;
        _directory = options.Directory ?? AppPaths.ConversationDirectory;

        if (_options.Enabled)
        {
            Directory.CreateDirectory(_directory);
        }
        else
        {
            _logger.ConversationLoggingDisabled();
        }
    }

    /// <inheritdoc />
    public Task LogRequestAsync(ConversationRequest request, CancellationToken cancellationToken = default)
        => WriteAsync(
            () => ConversationEventFactory.FromRequest(request, _options.MaxFieldChars),
            request.ConversationId,
            cancellationToken);

    /// <inheritdoc />
    public Task LogResponseAsync(ConversationResponse response, CancellationToken cancellationToken = default)
        => WriteAsync(
            () => ConversationEventFactory.FromResponse(response, _options.MaxFieldChars),
            response.ConversationId,
            cancellationToken);

    /// <inheritdoc />
    public Task LogFailureAsync(ConversationFailure failure, CancellationToken cancellationToken = default)
        => WriteAsync(
            () => ConversationEventFactory.FromFailure(failure, _options.MaxFieldChars),
            failure.ConversationId,
            cancellationToken);

    private async Task WriteAsync(
        Func<IReadOnlyList<ConversationEvent>> buildEvents,
        string conversationId,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            await _index.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            var events = buildEvents();
            if (events.Count == 0)
            {
                return;
            }

            var fileName = ConversationFileNames.FileName(conversationId) + ConversationFileNames.Extension;
            var lines = events.Select(evt => JsonSerializer.Serialize(evt, _jsonOptions)).ToList();

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await File.AppendAllLinesAsync(
                    ConversationFileNames.PathFor(_directory, conversationId), lines, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            foreach (var evt in events)
            {
                await _index.RecordEventAsync(evt, cancellationToken).ConfigureAwait(false);
            }

            var summary = await _index.TouchAsync(conversationId, events[^1].Timestamp, cancellationToken).ConfigureAwait(false);
            await _index.PersistAsync(summary, cancellationToken).ConfigureAwait(false);

            _logger.ConversationWritten(fileName, events.Count);
        }
        catch (Exception ex)
        {
            _logger.ConversationLoggingFailed(conversationId, ex);
        }
    }
}
