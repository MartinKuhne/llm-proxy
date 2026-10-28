namespace LlmProxy.Tests;

using System.Text.Json;
using LlmProxy.Application;
using LlmProxy.Application.Services;
using LlmProxy.Domain;
using Microsoft.Extensions.Logging;
using NSubstitute;

[Collection("ConversationAuditLog")]
public class ConversationIndexTests : IDisposable
{
    private static readonly JsonSerializerOptions _camelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly DateTimeOffset _start = new(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);

    private readonly string _directory;

    public ConversationIndexTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "llm-proxy-conversation-index", Guid.NewGuid().ToString("N"));
        AppPaths.ConversationDirectory = _directory;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public async Task TouchAsync_CreatesSummaryWithStartAndLastActivity()
    {
        var index = CreateIndex();

        var summary = await index.TouchAsync("conv-1", _start, CancellationToken.None);

        Assert.Equal("conv-1", summary.Id);
        Assert.Equal(_start, summary.StartedAt);
        Assert.Equal(_start, summary.LastActivityAt);
        Assert.Same(summary, await index.TouchAsync("conv-1", _start.AddMinutes(1), CancellationToken.None));
    }

    [Fact]
    public async Task TouchAsync_KeepsTheEarliestStartedAt()
    {
        var index = CreateIndex();

        await index.TouchAsync("conv-1", _start, CancellationToken.None);
        var summary = await index.TouchAsync("conv-1", _start.AddHours(1), CancellationToken.None);

        Assert.Equal(_start, summary.StartedAt);
        Assert.Equal(_start.AddHours(1), summary.LastActivityAt);
    }

    [Fact]
    public async Task RecordEventAsync_CountsEveryEventButOnlyCountsRequestsAsTurns()
    {
        var index = CreateIndex();

        await index.RecordEventAsync(Event(ConversationEventKind.Request, "conv-1", _start), CancellationToken.None);
        await index.RecordEventAsync(Event(ConversationEventKind.Thinking, "conv-1", _start.AddMinutes(1)), CancellationToken.None);
        await index.RecordEventAsync(Event(ConversationEventKind.Response, "conv-1", _start.AddMinutes(2)), CancellationToken.None);
        await index.RecordEventAsync(Event(ConversationEventKind.Request, "conv-1", _start.AddMinutes(3)), CancellationToken.None);

        var summary = Assert.Single(index.All());
        Assert.Equal(4, summary.EventCount);
        Assert.Equal(2, summary.TurnCount);
    }

    [Fact]
    public async Task RecordEventAsync_KeepsTheFirstProviderAndModel()
    {
        var index = CreateIndex();

        await index.RecordEventAsync(Event(ConversationEventKind.Request, "conv-1", _start, "provider-a", "model-a"), CancellationToken.None);
        await index.RecordEventAsync(Event(ConversationEventKind.Response, "conv-1", _start.AddMinutes(1), "provider-b", "model-b"), CancellationToken.None);

        var summary = Assert.Single(index.All());
        Assert.Equal("provider-a", summary.Provider);
        Assert.Equal("model-a", summary.Model);
    }

    [Fact]
    public async Task RecordEventAsync_FillsProviderAndModelFromALaterEventWhenEmpty()
    {
        var index = CreateIndex();

        await index.RecordEventAsync(Event(ConversationEventKind.Thinking, "conv-1", _start), CancellationToken.None);
        await index.RecordEventAsync(Event(ConversationEventKind.Response, "conv-1", _start.AddMinutes(1), "provider-b", "model-b"), CancellationToken.None);

        var summary = Assert.Single(index.All());
        Assert.Equal("provider-b", summary.Provider);
        Assert.Equal("model-b", summary.Model);
    }

    [Fact]
    public async Task RecordEventAsync_SetsTitleFromTheFirstUserMessage()
    {
        var index = CreateIndex();

        await index.RecordEventAsync(
            Event(ConversationEventKind.Request, "conv-1", _start, data: RequestData("How do I reset my password?")),
            CancellationToken.None);
        await index.RecordEventAsync(
            Event(ConversationEventKind.Request, "conv-1", _start.AddMinutes(1), data: RequestData("and again")),
            CancellationToken.None);

        Assert.Equal("How do I reset my password?", Assert.Single(index.All()).Title);
    }

    [Fact]
    public async Task RecordEventAsync_TruncatesLongTitles()
    {
        var index = CreateIndex();

        await index.RecordEventAsync(
            Event(ConversationEventKind.Request, "conv-1", _start, data: RequestData(new string('z', 300))),
            CancellationToken.None);

        Assert.Equal(80, Assert.Single(index.All()).Title!.Length);
    }

    [Fact]
    public async Task RecordEventAsync_LeavesTitleEmptyWithoutAUserMessage()
    {
        var index = CreateIndex();

        await index.RecordEventAsync(Event(ConversationEventKind.Request, "conv-1", _start, data: RequestData("system only", role: "system")), CancellationToken.None);

        Assert.Null(Assert.Single(index.All()).Title);
    }

    [Fact]
    public async Task RecordEventAsync_MarksErrorsAndAccumulatesTokens()
    {
        var index = CreateIndex();

        await index.RecordEventAsync(
            Event(ConversationEventKind.Response, "conv-1", _start, data: Json("""{"usage":{"total_tokens":10}}""")),
            CancellationToken.None);
        await index.RecordEventAsync(
            Event(ConversationEventKind.Response, "conv-1", _start.AddMinutes(1), data: Json("""{"usage":{"total_tokens":32}}""")),
            CancellationToken.None);
        await index.RecordEventAsync(Event(ConversationEventKind.Error, "conv-1", _start.AddMinutes(2)), CancellationToken.None);

        var summary = Assert.Single(index.All());
        Assert.True(summary.HasErrors);
        Assert.Equal(42, summary.TotalTokens);
    }

    [Fact]
    public async Task PersistAsync_ThenReload_KeepsTheLastWrite()
    {
        var index = CreateIndex();
        await index.PersistAsync(Summary("conv-1", eventCount: 1), CancellationToken.None);
        await index.PersistAsync(Summary("conv-1", eventCount: 7), CancellationToken.None);

        var reloaded = CreateIndex();
        await reloaded.EnsureLoadedAsync(CancellationToken.None);

        var summary = Assert.Single(reloaded.All());
        Assert.Equal(7, summary.EventCount);
    }

    [Fact]
    public async Task EnsureLoadedAsync_RebuildsFromConversationFilesWhenIndexIsAbsent()
    {
        WriteConversationFile("alpha.jsonl", Event(ConversationEventKind.Request, "alpha", _start, "provider-a", "model-a"));
        WriteConversationFile("beta.jsonl", Event(ConversationEventKind.Response, "beta", _start.AddMinutes(1), "provider-b", "model-b"));

        var index = CreateIndex();
        await index.EnsureLoadedAsync(CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(_directory, "index.jsonl")));

        var summaries = index.All();
        Assert.Equal(2, summaries.Count);
        Assert.Equal("beta", summaries[0].Id);
        Assert.Equal("alpha", summaries[1].Id);
        Assert.Equal("provider-b", summaries[0].Provider);
        Assert.Equal(1, summaries[1].TurnCount);
    }

    [Fact]
    public async Task RebuildAsync_SkipsTheIndexFileItself()
    {
        WriteConversationFile("alpha.jsonl", Event(ConversationEventKind.Request, "alpha", _start));
        File.AppendAllLines(
            Path.Combine(_directory, "index.jsonl"),
            new[] { JsonSerializer.Serialize(Summary("from-index", 99), _camelCase) });

        var index = CreateIndex();
        await index.RebuildAsync(CancellationToken.None);

        Assert.Equal("alpha", Assert.Single(index.All()).Id);
    }

    [Fact]
    public async Task EnsureLoadedAsync_PrefersTheIndexFileOverRebuilding()
    {
        WriteConversationFile("alpha.jsonl", Event(ConversationEventKind.Request, "alpha", _start));
        File.AppendAllLines(
            Path.Combine(_directory, "index.jsonl"),
            new[] { JsonSerializer.Serialize(Summary("indexed-only", 5), _camelCase) });

        var index = CreateIndex();
        await index.EnsureLoadedAsync(CancellationToken.None);

        var summary = Assert.Single(index.All());
        Assert.Equal("indexed-only", summary.Id);
        Assert.Equal(5, summary.EventCount);
    }

    [Fact]
    public async Task EnsureLoadedAsync_SkipsCorruptLinesWithoutThrowing()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "alpha.jsonl");
        File.AppendAllLines(path, new[]
        {
            "{ this is not json",
            string.Empty,
            JsonSerializer.Serialize(Event(ConversationEventKind.Request, "alpha", _start), _camelCase),
            JsonSerializer.Serialize(new { kind = "request" }, _camelCase),
            "[]",
        });

        var index = CreateIndex();
        await index.EnsureLoadedAsync(CancellationToken.None);

        var summary = Assert.Single(index.All());
        Assert.Equal("alpha", summary.Id);
        Assert.Equal(1, summary.EventCount);
    }

    [Fact]
    public async Task EnsureLoadedAsync_LoadsOnlyOnce()
    {
        WriteConversationFile("alpha.jsonl", Event(ConversationEventKind.Request, "alpha", _start));

        var index = CreateIndex();
        await index.EnsureLoadedAsync(CancellationToken.None);

        WriteConversationFile("beta.jsonl", Event(ConversationEventKind.Request, "beta", _start));
        await index.EnsureLoadedAsync(CancellationToken.None);

        Assert.Equal("alpha", Assert.Single(index.All()).Id);
    }

    private static ConversationIndex CreateIndex() => new(Substitute.For<ILogger<ConversationIndex>>());

    private void WriteConversationFile(string fileName, params ConversationEvent[] events)
    {
        Directory.CreateDirectory(_directory);
        File.AppendAllLines(
            Path.Combine(_directory, fileName),
            events.Select(evt => JsonSerializer.Serialize(evt, _camelCase)));
    }

    private static ConversationSummary Summary(string id, int eventCount) =>
        new()
        {
            Id = id,
            StartedAt = _start,
            LastActivityAt = _start.AddMinutes(eventCount),
            EventCount = eventCount,
            TurnCount = eventCount,
            Provider = "provider-a",
            Model = "model-a",
            Title = "title",
            TotalTokens = 100,
        };

    private static ConversationEvent Event(
        string kind,
        string conversationId,
        DateTimeOffset timestamp,
        string? provider = null,
        string? model = null,
        JsonElement? data = null) =>
        new(kind, timestamp, conversationId, "req-1", provider, model, "/v1/chat/completions", data);

    private static JsonElement RequestData(string userContent, string role = "user") =>
        Json($$"""{"body":{"messages":[{"role":"{{role}}","content":{{JsonSerializer.Serialize(userContent)}}}]},"messageCount":1}""");

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
