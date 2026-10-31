namespace LlmProxy.Tests;

using System.Text.Json;
using LlmProxy.Application;
using LlmProxy.Application.Services;
using LlmProxy.Domain;
using Microsoft.Extensions.Logging;
using NSubstitute;

/// <summary>
/// Listing the conversation audit log: ordering, date and provider filters, search and paging.
/// </summary>
[Collection("ConversationAuditLog")]
public class ConversationReaderListTests : IDisposable
{
    private static readonly DateTimeOffset _day1 = new(2026, 10, 25, 23, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _day2 = new(2026, 10, 26, 8, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _day3 = new(2026, 10, 27, 9, 14, 2, TimeSpan.Zero);

    private readonly string _directory;
    private readonly ConversationIndex _index;
    private readonly ConversationLocks _locks;
    private readonly FileConversationReader _reader;

    public ConversationReaderListTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "llm-proxy-conversation-reader-list", Guid.NewGuid().ToString("N"));
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
    public async Task ListAsync_OrdersByMostRecentActivityFirst()
    {
        await SeedAsync("old", "old chat", "OpenAI", "gpt-4", _day1);
        await SeedAsync("new", "new chat", "Anthropic", "claude-3", _day3);
        await SeedAsync("mid", "mid chat", "OpenAI", "gpt-4", _day2);

        var page = await _reader.ListAsync(new ConversationQuery(), CancellationToken.None);

        Assert.Equal(new[] { "new", "mid", "old" }, Ids(page));
    }

    [Fact]
    public async Task ListAsync_DateRangeIsInclusiveOnBothEnds()
    {
        await SeedAsync("d1", "one", "OpenAI", "gpt-4", _day1);
        await SeedAsync("d2", "two", "OpenAI", "gpt-4", _day2);
        await SeedAsync("d3", "three", "OpenAI", "gpt-4", _day3);

        var both = await _reader.ListAsync(
            new ConversationQuery(from: DateOnly.FromDateTime(_day1.UtcDateTime), to: DateOnly.FromDateTime(_day3.UtcDateTime)),
            CancellationToken.None);
        Assert.Equal(new[] { "d3", "d2", "d1" }, Ids(both));

        var fromSecondDay = await _reader.ListAsync(
            new ConversationQuery(from: DateOnly.FromDateTime(_day2.UtcDateTime)), CancellationToken.None);
        Assert.Equal(new[] { "d3", "d2" }, Ids(fromSecondDay));

        var toSecondDay = await _reader.ListAsync(
            new ConversationQuery(to: DateOnly.FromDateTime(_day2.UtcDateTime)), CancellationToken.None);
        Assert.Equal(new[] { "d2", "d1" }, Ids(toSecondDay));

        var singleDay = await _reader.ListAsync(
            new ConversationQuery(
                from: DateOnly.FromDateTime(_day2.UtcDateTime),
                to: DateOnly.FromDateTime(_day2.UtcDateTime)),
            CancellationToken.None);
        Assert.Equal(new[] { "d2" }, Ids(singleDay));
    }

    [Fact]
    public async Task ListAsync_DateFilterLooksAtLastActivityNotAtTheStartOfTheConversation()
    {
        var logger = CreateLogger();
        await logger.LogRequestAsync(
            Request("spanning", "started early", "OpenAI", "gpt-4", _day1), CancellationToken.None);
        await logger.LogResponseAsync(
            Response("spanning", "OpenAI", "gpt-4", _day3), CancellationToken.None);

        var page = await _reader.ListAsync(
            new ConversationQuery(to: DateOnly.FromDateTime(_day2.UtcDateTime)), CancellationToken.None);

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task ListAsync_ProviderFilterIsExactAndCaseInsensitive()
    {
        await SeedAsync("a1", "first", "OpenAI", "gpt-4", _day1);
        await SeedAsync("a2", "second", "openai", "gpt-4", _day2);
        await SeedAsync("b1", "third", "Anthropic", "claude-3", _day3);

        var lower = await _reader.ListAsync(new ConversationQuery(provider: "openai"), CancellationToken.None);
        Assert.Equal(new[] { "a2", "a1" }, Ids(lower));

        var upper = await _reader.ListAsync(new ConversationQuery(provider: "OPENAI"), CancellationToken.None);
        Assert.Equal(new[] { "a2", "a1" }, Ids(upper));

        var partial = await _reader.ListAsync(new ConversationQuery(provider: "pen"), CancellationToken.None);
        Assert.Empty(partial.Items);
    }

    [Theory]
    [InlineData("middleware", "alpha")]
    [InlineData("bravo", "bravo")]
    [InlineData("anthropic", "charlie")]
    [InlineData("GEMINI", "delta")]
    public async Task ListAsync_SearchMatchesTitleIdProviderAndModel(string search, string expectedId)
    {
        await SeedAsync("alpha", "Refactor the auth middleware", "OpenAI", "gpt-4", _day3);
        await SeedAsync("bravo", "What is the weather", "OpenAI", "gpt-4", _day2);
        await SeedAsync("charlie", "Best pasta recipe", "Anthropic", "claude-3", _day1);
        await SeedAsync("delta", "Write a haiku", "Google", "gemini-pro", _day1.AddHours(1));

        var page = await _reader.ListAsync(new ConversationQuery(search: search), CancellationToken.None);

        Assert.Equal(new[] { expectedId }, Ids(page));
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task ListAsync_PagesThroughTheMatchesNewestFirst()
    {
        for (var i = 1; i <= 5; i++)
        {
            await SeedAsync($"c{i}", $"chat {i}", "OpenAI", "gpt-4", _day1.AddHours(i));
        }

        var first = await _reader.ListAsync(new ConversationQuery(page: 1, pageSize: 2), CancellationToken.None);
        Assert.Equal(new[] { "c5", "c4" }, Ids(first));
        Assert.Equal(1, first.Page);
        Assert.Equal(2, first.PageSize);
        Assert.Equal(5, first.TotalCount);
        Assert.Equal(3, first.TotalPages);

        var second = await _reader.ListAsync(new ConversationQuery(page: 2, pageSize: 2), CancellationToken.None);
        Assert.Equal(new[] { "c3", "c2" }, Ids(second));
        Assert.Equal(5, second.TotalCount);
        Assert.Equal(3, second.TotalPages);

        var third = await _reader.ListAsync(new ConversationQuery(page: 3, pageSize: 2), CancellationToken.None);
        Assert.Equal(new[] { "c1" }, Ids(third));
    }

    [Fact]
    public async Task ListAsync_PageBeyondTheLast_ReturnsNoItemsButKeepsTheTotals()
    {
        await SeedAsync("a", "a", "OpenAI", "gpt-4", _day1);
        await SeedAsync("b", "b", "OpenAI", "gpt-4", _day2);

        var page = await _reader.ListAsync(new ConversationQuery(page: 9, pageSize: 2), CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(9, page.Page);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(1, page.TotalPages);
    }

    [Fact]
    public async Task ListAsync_WithNoConversations_ReturnsTheEmptyShape()
    {
        var page = await _reader.ListAsync(new ConversationQuery(), CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(1, page.Page);
        Assert.Equal(50, page.PageSize);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.TotalPages);
    }

    [Fact]
    public async Task ListAsync_NormalisesPagingSentByAClient()
    {
        await SeedAsync("only", "only chat", "OpenAI", "gpt-4", _day1);

        var zero = await _reader.ListAsync(new ConversationQuery(page: 0, pageSize: 0), CancellationToken.None);
        Assert.Equal(1, zero.Page);
        Assert.Equal(50, zero.PageSize);
        Assert.Single(zero.Items);

        var huge = await _reader.ListAsync(new ConversationQuery(page: -3, pageSize: 5000), CancellationToken.None);
        Assert.Equal(1, huge.Page);
        Assert.Equal(200, huge.PageSize);
    }

    [Theory]
    [InlineData(0, 0, 1, 50)]
    [InlineData(-7, 0, 1, 50)]
    [InlineData(1, 5000, 1, 200)]
    [InlineData(4, 25, 4, 25)]
    public void ConversationQuery_NormalisesPagingOnConstruction(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        var query = new ConversationQuery(page: page, pageSize: pageSize);

        Assert.Equal(expectedPage, query.Page);
        Assert.Equal(expectedPageSize, query.PageSize);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConversationQuery_TreatsBlankFiltersAsNoFilter(string value)
    {
        var query = new ConversationQuery(provider: value, search: value);

        Assert.Null(query.Provider);
        Assert.Null(query.Search);
    }

    [Fact]
    public async Task ListAsync_MapsEverySummaryFieldOntoTheItem()
    {
        await SeedAsync("rich", "Explain dependency injection", "OpenAI", "gpt-4", _day3, withError: true);

        var item = Assert.Single((await _reader.ListAsync(new ConversationQuery(), CancellationToken.None)).Items);

        Assert.Equal("rich", item.Id);
        Assert.Equal("Explain dependency injection", item.Title);
        Assert.Equal(_day3 - TimeSpan.FromMinutes(1), item.StartedAt);
        Assert.Equal(_day3, item.LastActivityAt);
        Assert.Equal(1, item.TurnCount);
        Assert.Equal(3, item.EventCount);
        Assert.Equal("OpenAI", item.Provider);
        Assert.Equal("gpt-4", item.Model);
        Assert.Equal(42, item.TotalTokens);
        Assert.True(item.HasErrors);
    }

    private static IEnumerable<string> Ids(ConversationPage page) => page.Items.Select(item => item.Id);

    private FileConversationLogger CreateLogger() =>
        new(
            Substitute.For<ILogger<FileConversationLogger>>(),
            _index,
            _locks,
            new ConversationOptions { Directory = _directory });

    private async Task SeedAsync(
        string id,
        string title,
        string provider,
        string model,
        DateTimeOffset lastActivity,
        bool withError = false)
    {
        var logger = CreateLogger();

        await logger.LogRequestAsync(
            Request(id, title, provider, model, lastActivity - TimeSpan.FromMinutes(1)), CancellationToken.None);
        await logger.LogResponseAsync(
            Response(id, provider, model, lastActivity), CancellationToken.None);

        if (withError)
        {
            await logger.LogFailureAsync(
                new ConversationFailure(id, "req-2", lastActivity, provider, model, "/v1/chat/completions", 503, "no capacity"),
                CancellationToken.None);
        }
    }

    private static ConversationRequest Request(string id, string title, string provider, string model, DateTimeOffset at) =>
        new(
            id,
            "req-1",
            at,
            provider,
            model,
            "/v1/chat/completions",
            Json($$"""{"model":"{{model}}","messages":[{"role":"user","content":{{JsonSerializer.Serialize(title)}}}]}"""));

    private static ConversationResponse Response(string id, string provider, string model, DateTimeOffset at) =>
        new(
            id,
            "req-1",
            at,
            provider,
            model,
            "/v1/chat/completions",
            Json("""{"role":"assistant","content":"done","finish_reason":"stop"}"""),
            Json("""{"total_tokens":42}"""),
            false);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
