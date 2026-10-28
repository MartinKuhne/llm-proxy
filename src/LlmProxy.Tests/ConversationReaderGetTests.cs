namespace LlmProxy.Tests;

using System.Text.Json;
using LlmProxy.Application;
using LlmProxy.Application.Services;
using LlmProxy.Domain;
using Microsoft.Extensions.Logging;
using NSubstitute;

/// <summary>
/// Reading one conversation back: event order, corrupt lines, unknown ids and the summary fallback.
/// </summary>
[Collection("ConversationAuditLog")]
public class ConversationReaderGetTests : IDisposable
{
    private static readonly DateTimeOffset _start = new(2026, 10, 27, 9, 14, 2, TimeSpan.Zero);
    private static readonly JsonSerializerOptions _camelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _directory;
    private readonly ConversationIndex _index;
    private readonly ConversationLocks _locks;
    private readonly FileConversationReader _reader;

    public ConversationReaderGetTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "llm-proxy-conversation-reader-get", Guid.NewGuid().ToString("N"));
        AppPaths.ConversationDirectory = _directory;
        _index = new ConversationIndex(Substitute.For<ILogger<ConversationIndex>>());
        _locks = new ConversationLocks();
        _reader = new FileConversationReader(Substitute.For<ILogger<FileConversationReader>>(), _index);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public async Task GetAsync_ReturnsEveryEventInFileOrderWithItsKind()
    {
        var logger = CreateLogger();
        await logger.LogRequestAsync(RequestWithToolResult(), CancellationToken.None);
        await logger.LogResponseAsync(ResponseWithToolCall(), CancellationToken.None);

        var detail = await _reader.GetAsync("alpha", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(
            new[]
            {
                ConversationEventKind.Request,
                ConversationEventKind.ToolResult,
                ConversationEventKind.Thinking,
                ConversationEventKind.ToolCall,
                ConversationEventKind.Response,
            },
            detail.Events.Select(evt => evt.Kind).ToArray());

        Assert.All(detail.Events, evt => Assert.Equal("alpha", evt.ConversationId));
        Assert.All(detail.Events, evt => Assert.Equal("req-1", evt.RequestId));

        var toolCall = detail.Events.Single(evt => evt.Kind == ConversationEventKind.ToolCall);
        Assert.Equal("read_file", toolCall.Data!.Value.GetProperty("name").GetString());
        Assert.Equal("call_1", toolCall.Data.Value.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetAsync_SkipsACorruptLineInTheMiddleWithoutFailingTheRead()
    {
        WriteConversationFile(
            "alpha.jsonl",
            new[]
            {
                Serialize(Event(ConversationEventKind.Request, _start)),
                "{ this line is not json",
                string.Empty,
                Serialize(Event(ConversationEventKind.Response, _start.AddMinutes(1))),
            });

        var detail = await _reader.GetAsync("alpha", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(
            new[] { ConversationEventKind.Request, ConversationEventKind.Response },
            detail.Events.Select(evt => evt.Kind).ToArray());
    }

    [Fact]
    public async Task GetAsync_UnknownConversation_ReturnsNull()
    {
        Assert.Null(await _reader.GetAsync("nope", CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_MissingFileForAKnownIndexEntry_ReturnsNull()
    {
        await _index.TouchAsync("ghost", _start, CancellationToken.None);

        Assert.Null(await _reader.GetAsync("ghost", CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_ConversationTheIndexDoesNotKnow_StillReturnsAUsableDetail()
    {
        WriteConversationFile(
            "orphan.jsonl",
            new[]
            {
                Serialize(Event(
                    ConversationEventKind.Request,
                    _start,
                    provider: "OpenAI",
                    model: "gpt-4",
                    data: """{"body":{"messages":[{"role":"user","content":"What is the airspeed of a swallow?"}]},"messageCount":1}""")),
                Serialize(Event(
                    ConversationEventKind.Response,
                    _start.AddMinutes(3),
                    provider: "OpenAI",
                    model: "gpt-4",
                    data: """{"content":"African or European?","finishReason":"stop","streamed":false,"usage":{"total_tokens":42}}""")),
            });

        var detail = await _reader.GetAsync("orphan", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("orphan", detail.Id);
        Assert.Equal("What is the airspeed of a swallow?", detail.Title);
        Assert.Equal(_start, detail.StartedAt);
        Assert.Equal(_start.AddMinutes(3), detail.LastActivityAt);
        Assert.Equal(1, detail.TurnCount);
        Assert.Equal(2, detail.EventCount);
        Assert.Equal("OpenAI", detail.Provider);
        Assert.Equal("gpt-4", detail.Model);
        Assert.Equal(42, detail.TotalTokens);
        Assert.False(detail.HasErrors);
        Assert.Equal(2, detail.Events.Count);
    }

    [Fact]
    public async Task GetAsync_ConversationTheIndexKnows_UsesTheIndexSummary()
    {
        var logger = CreateLogger();
        await logger.LogRequestAsync(RequestWithToolResult(), CancellationToken.None);
        await logger.LogResponseAsync(ResponseWithToolCall(), CancellationToken.None);

        var detail = await _reader.GetAsync("alpha", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("Read the file", detail.Title);
        Assert.Equal(1, detail.TurnCount);
        Assert.Equal(5, detail.EventCount);
        Assert.Equal(7, detail.TotalTokens);
        Assert.Equal(_start, detail.StartedAt);
        Assert.Equal(_start.AddMinutes(1), detail.LastActivityAt);
    }

    [Fact]
    public async Task GetAsync_ConversationWithError_IsFlagged()
    {
        var logger = CreateLogger();
        await logger.LogFailureAsync(
            new ConversationFailure("alpha", "req-1", _start, "provider-a", "gpt-test", "/v1/chat/completions", 429, "slow down"),
            CancellationToken.None);

        var detail = await _reader.GetAsync("alpha", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.True(detail.HasErrors);
        Assert.Equal(429, detail.Events.Single().Data!.Value.GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task GetAsync_EmptyConversationFile_ReturnsADetailWithoutEvents()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "empty.jsonl"), string.Empty);

        var detail = await _reader.GetAsync("empty", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Empty(detail.Events);
        Assert.Equal(0, detail.EventCount);
    }

    [Theory]
    [InlineData("../../evil")]
    [InlineData("..\\..\\evil")]
    [InlineData("/etc/passwd")]
    public async Task GetAsync_NeverReadsOutsideTheConversationDirectory(string conversationId)
    {
        var resolved = Path.GetFullPath(ConversationFileNames.PathFor(_directory, conversationId));

        Assert.StartsWith(
            Path.GetFullPath(_directory) + Path.DirectorySeparatorChar,
            resolved,
            StringComparison.Ordinal);
        Assert.Null(await _reader.GetAsync(conversationId, CancellationToken.None));
    }

    private FileConversationLogger CreateLogger() =>
        new(
            Substitute.For<ILogger<FileConversationLogger>>(),
            _index,
            _locks,
            new ConversationOptions { Directory = _directory });

    private void WriteConversationFile(string fileName, IEnumerable<string> lines)
    {
        Directory.CreateDirectory(_directory);
        File.AppendAllLines(Path.Combine(_directory, fileName), lines);
    }

    private static string Serialize(ConversationEvent evt) => JsonSerializer.Serialize(evt, _camelCase);

    private static ConversationEvent Event(
        string kind,
        DateTimeOffset timestamp,
        string? provider = "provider-a",
        string? model = "gpt-test",
        string? data = null) =>
        new(kind, timestamp, "alpha", "req-1", provider, model, "/v1/chat/completions", data is null ? null : Json(data));

    private static ConversationRequest RequestWithToolResult() =>
        new(
            "alpha",
            "req-1",
            _start,
            "provider-a",
            "gpt-test",
            "/v1/chat/completions",
            Json("""
                {"model":"gpt-test","messages":[{"role":"user","content":"Read the file"},{"role":"tool","tool_call_id":"call_1","name":"read_file","content":"class Widget {}"}]}
                """));

    private static ConversationResponse ResponseWithToolCall() =>
        new(
            "alpha",
            "req-1",
            _start.AddMinutes(1),
            "provider-a",
            "gpt-test",
            "/v1/chat/completions",
            Json("""
                {"role":"assistant","reasoning":"I should read the file first","tool_calls":[{"id":"call_1","type":"function","function":{"name":"read_file","arguments":"{\"path\":\"a.cs\"}"}}],"finish_reason":"tool_calls"}
                """),
            Json("""{"total_tokens":7}"""),
            false);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
