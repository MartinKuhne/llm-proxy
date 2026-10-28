namespace LlmProxy.Tests;

using System.Text.Json;
using LlmProxy.Api.Controllers;
using LlmProxy.Api.Responses;
using LlmProxy.Application.Services;
using LlmProxy.Domain;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit.Abstractions;

/// <summary>
/// The conversations controller: query mapping, DTO shape and the 404 envelope.
/// </summary>
public class ConversationsControllerTests
{
    private static readonly DateTimeOffset _started = new(2026, 10, 27, 9, 14, 2, TimeSpan.Zero);
    private static readonly DateTimeOffset _lastActivity = new(2026, 10, 27, 9, 31, 47, TimeSpan.Zero);

    private readonly ITestOutputHelper _output;
    private readonly IConversationReader _reader;
    private readonly ConversationsController _controller;
    private readonly List<ConversationQuery> _receivedQueries = new();

    public ConversationsControllerTests(ITestOutputHelper output)
    {
        _output = output;
        _reader = Substitute.For<IConversationReader>();
        _reader
            .ListAsync(Arg.Any<ConversationQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _receivedQueries.Add(call.Arg<ConversationQuery>());
                return new ConversationPage(new List<ConversationSummary>(), 1, 50, 0, 0);
            });

        _controller = new ConversationsController(_reader);
    }

    [Fact]
    public async Task List_MapsEveryQueryStringParameterOntoTheQuery()
    {
        await _controller.List("2026-10-01", "2026-10-31", 2, 25, "OpenAI", "auth", CancellationToken.None);

        var query = Assert.Single(_receivedQueries);
        Assert.Equal(new DateOnly(2026, 10, 1), query.From);
        Assert.Equal(new DateOnly(2026, 10, 31), query.To);
        Assert.Equal(2, query.Page);
        Assert.Equal(25, query.PageSize);
        Assert.Equal("OpenAI", query.Provider);
        Assert.Equal("auth", query.Search);
    }

    [Fact]
    public async Task List_WithoutAnyParameters_UsesTheDefaults()
    {
        await _controller.List(null, null, null, null, null, null, CancellationToken.None);

        var query = Assert.Single(_receivedQueries);
        Assert.Null(query.From);
        Assert.Null(query.To);
        Assert.Equal(1, query.Page);
        Assert.Equal(50, query.PageSize);
        Assert.Null(query.Provider);
        Assert.Null(query.Search);
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("31-10-2026")]
    [InlineData("2026/10/31 09:00")]
    [InlineData("")]
    public async Task List_TreatsAnUnparseableFromDateAsNoFilter(string from)
    {
        await _controller.List(from, null, null, null, null, null, CancellationToken.None);

        var query = Assert.Single(_receivedQueries);
        Assert.Null(query.From);
    }

    [Fact]
    public async Task List_MapsTheSummariesOntoTheDto()
    {
        _reader
            .ListAsync(Arg.Any<ConversationQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ConversationPage(new[] { Summary() }, 3, 10, 137, 14));

        var dto = await _controller.List(null, null, 3, 10, null, null, CancellationToken.None);

        Assert.Equal(3, dto.Page);
        Assert.Equal(10, dto.PageSize);
        Assert.Equal(137, dto.TotalCount);
        Assert.Equal(14, dto.TotalPages);

        var item = Assert.Single(dto.Items);
        Assert.Equal("3f2a1c9b7e04d8a6", item.Id);
        Assert.Equal("Refactor the auth middleware", item.Title);
        Assert.Equal(_started, item.StartedAt);
        Assert.Equal(_lastActivity, item.LastActivityAt);
        Assert.Equal(12, item.TurnCount);
        Assert.Equal(41, item.EventCount);
        Assert.Equal("OpenAI", item.Provider);
        Assert.Equal("gpt-4", item.Model);
        Assert.Equal(18342, item.TotalTokens);
        Assert.False(item.HasErrors);
    }

    [Fact]
    public async Task List_SerialisesTheDocumentedCamelCaseShape()
    {
        _reader
            .ListAsync(Arg.Any<ConversationQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ConversationPage(new[] { Summary() }, 1, 50, 137, 3));

        var dto = await _controller.List(null, null, null, null, null, null, CancellationToken.None);
        var json = JsonSerializer.Serialize(dto, WireJson());
        _output.WriteLine(json);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            new[] { "items", "page", "pageSize", "totalCount", "totalPages" },
            Names(document.RootElement));
        Assert.Equal(
            new[]
            {
                "id", "title", "startedAt", "lastActivityAt", "turnCount",
                "eventCount", "provider", "model", "totalTokens", "hasErrors",
            },
            Names(document.RootElement.GetProperty("items")[0]));
    }

    [Fact]
    public async Task Get_MapsTheDetailAndItsEventsOntoTheDto()
    {
        _reader
            .GetAsync("3f2a1c9b7e04d8a6", Arg.Any<CancellationToken>())
            .Returns(new ConversationDetail(
                "3f2a1c9b7e04d8a6",
                "Refactor the auth middleware",
                _started,
                _lastActivity,
                12,
                41,
                "OpenAI",
                "gpt-4",
                18342,
                false,
                new[]
                {
                    new ConversationEvent(
                        ConversationEventKind.Request,
                        _started,
                        "3f2a1c9b7e04d8a6",
                        "b1a2c3d4",
                        "OpenAI",
                        "gpt-4",
                        "/v1/chat/completions",
                        Json("""{"body":{"messages":[{"role":"user","content":"Refactor the auth middleware"}]},"messageCount":1}""")),
                    new ConversationEvent(
                        ConversationEventKind.Error,
                        _lastActivity,
                        "3f2a1c9b7e04d8a6",
                        "b1a2c3d4",
                        null,
                        null,
                        null,
                        Json("""{"statusCode":429,"body":"slow down"}""")),
                }));

        var result = await _controller.Get("3f2a1c9b7e04d8a6", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<ConversationDetailDto>(ok.Value);

        Assert.Equal("3f2a1c9b7e04d8a6", dto.Id);
        Assert.Equal("Refactor the auth middleware", dto.Title);
        Assert.Equal(12, dto.TurnCount);
        Assert.Equal(41, dto.EventCount);
        Assert.Equal(18342, dto.TotalTokens);
        Assert.False(dto.HasErrors);
        Assert.Equal(2, dto.Events.Count);
        Assert.Equal("request", dto.Events[0].Kind);
        Assert.Equal("b1a2c3d4", dto.Events[0].RequestId);
        Assert.Equal("Refactor the auth middleware", dto.Events[0].Data!.Value.GetProperty("body").GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal(429, dto.Events[1].Data!.Value.GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task Get_SerialisesTheDocumentedCamelCaseShape()
    {
        _reader
            .GetAsync("3f2a1c9b7e04d8a6", Arg.Any<CancellationToken>())
            .Returns(new ConversationDetail(
                "3f2a1c9b7e04d8a6",
                "Refactor the auth middleware",
                _started,
                _lastActivity,
                12,
                41,
                "OpenAI",
                "gpt-4",
                18342,
                false,
                new[]
                {
                    new ConversationEvent(
                        ConversationEventKind.Request,
                        _started,
                        "3f2a1c9b7e04d8a6",
                        "b1a2c3d4",
                        "OpenAI",
                        "gpt-4",
                        "/v1/chat/completions",
                        Json("""{"body":{"messages":[{"role":"user","content":"Refactor the auth middleware"}]},"messageCount":1}""")),
                }));

        var result = await _controller.Get("3f2a1c9b7e04d8a6", CancellationToken.None);
        var json = JsonSerializer.Serialize(((OkObjectResult)result).Value, WireJson());
        _output.WriteLine(json);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            new[]
            {
                "id", "title", "startedAt", "lastActivityAt", "turnCount", "eventCount",
                "provider", "model", "totalTokens", "hasErrors", "events",
            },
            Names(document.RootElement));
        Assert.Equal(
            new[] { "kind", "timestamp", "requestId", "provider", "model", "path", "data" },
            Names(document.RootElement.GetProperty("events")[0]));
    }

    [Fact]
    public async Task Get_UnknownConversation_ReturnsTheErrorEnvelope()
    {
        _reader.GetAsync("missing", Arg.Any<CancellationToken>()).Returns((ConversationDetail?)null);

        var result = await _controller.Get("missing", CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var envelope = Assert.IsType<ErrorResponseDto>(notFound.Value);

        Assert.Equal("Conversation 'missing' was not found", envelope.Error.Message);
        Assert.Equal("not_found", envelope.Error.Type);
        Assert.Equal("id", envelope.Error.Param);
        Assert.Equal("404", envelope.Error.Code);

        var json = JsonSerializer.Serialize(envelope, WireJson());
        _output.WriteLine(json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(new[] { "error" }, Names(document.RootElement));
        Assert.Equal(new[] { "message", "type", "param", "code" }, Names(document.RootElement.GetProperty("error")));
    }

    private static ConversationSummary Summary() =>
        new()
        {
            Id = "3f2a1c9b7e04d8a6",
            Title = "Refactor the auth middleware",
            StartedAt = _started,
            LastActivityAt = _lastActivity,
            TurnCount = 12,
            EventCount = 41,
            Provider = "OpenAI",
            Model = "gpt-4",
            TotalTokens = 18342,
            HasErrors = false,
        };

    /// <summary>Same naming as the ASP.NET Core JSON defaults so the assertions describe the wire format.</summary>
    private static JsonSerializerOptions WireJson() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string[] Names(JsonElement element) =>
        [.. element.EnumerateObject().Select(property => property.Name)];
}
