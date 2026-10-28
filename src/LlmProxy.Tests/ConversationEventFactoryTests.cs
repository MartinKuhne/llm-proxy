namespace LlmProxy.Tests;

using System.Text.Json;
using LlmProxy.Application.Services;
using LlmProxy.Domain;

public class ConversationEventFactoryTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FromRequest_BuildsRequestEventWithBodyAndMessageCount()
    {
        var request = CreateRequest("""{"model":"gpt-test","messages":[{"role":"user","content":"hi"},{"role":"assistant","content":"hello"}]}""");

        var events = ConversationEventFactory.FromRequest(request, 1000);

        var evt = Assert.Single(events);
        Assert.Equal(ConversationEventKind.Request, evt.Kind);
        Assert.Equal("conv-1", evt.ConversationId);
        Assert.Equal("req-1", evt.RequestId);
        Assert.Equal("provider-a", evt.Provider);
        Assert.Equal("gpt-test", evt.Model);

        var data = evt.Data!.Value;
        Assert.Equal(2, data.GetProperty("messageCount").GetInt32());
        Assert.Equal("gpt-test", data.GetProperty("body").GetProperty("model").GetString());
    }

    [Fact]
    public void FromRequest_BuildsToolResultEventPerToolMessage()
    {
        var request = CreateRequest(
            """
            {"messages":[
              {"role":"user","content":"weather?"},
              {"role":"tool","tool_call_id":"call_1","name":"get_weather","content":"sunny"},
              {"role":"tool","tool_call_id":"call_2","content":"rain"},
              {"role":"user","content":"thanks"}
            ]}
            """);

        var events = ConversationEventFactory.FromRequest(request, 1000);

        Assert.Equal(3, events.Count);
        Assert.Equal(ConversationEventKind.Request, events[0].Kind);
        Assert.Equal(ConversationEventKind.ToolResult, events[1].Kind);
        Assert.Equal(ConversationEventKind.ToolResult, events[2].Kind);

        var first = events[1].Data!.Value;
        Assert.Equal("call_1", first.GetProperty("toolCallId").GetString());
        Assert.Equal("get_weather", first.GetProperty("name").GetString());
        Assert.Equal("sunny", first.GetProperty("content").GetString());

        var second = events[2].Data!.Value;
        Assert.Equal("call_2", second.GetProperty("toolCallId").GetString());
        Assert.False(second.TryGetProperty("name", out _));
        Assert.Equal("rain", second.GetProperty("content").GetString());
    }

    [Fact]
    public void FromRequest_WithoutMessages_StillBuildsRequestEvent()
    {
        var request = CreateRequest("""{"model":"gpt-test"}""");

        var events = ConversationEventFactory.FromRequest(request, 1000);

        var evt = Assert.Single(events);
        Assert.Equal(0, evt.Data!.Value.GetProperty("messageCount").GetInt32());
    }

    [Fact]
    public void FromResponse_WithReasoning_BuildsThinkingThenResponse()
    {
        var response = CreateResponse(
            """{"reasoning":"think hard","content":"the answer","finish_reason":"stop"}""",
            """{"total_tokens":12}""");

        var events = ConversationEventFactory.FromResponse(response, 1000);

        Assert.Equal(2, events.Count);
        Assert.Equal(ConversationEventKind.Thinking, events[0].Kind);
        Assert.Equal("think hard", events[0].Data!.Value.GetProperty("content").GetString());

        var data = events[1].Data!.Value;
        Assert.Equal(ConversationEventKind.Response, events[1].Kind);
        Assert.Equal("the answer", data.GetProperty("content").GetString());
        Assert.Equal("stop", data.GetProperty("finishReason").GetString());
        Assert.True(data.GetProperty("streamed").GetBoolean());
        Assert.Equal(12, data.GetProperty("usage").GetProperty("total_tokens").GetInt32());
    }

    [Fact]
    public void FromResponse_WithReasoningContent_BuildsThinkingEvent()
    {
        var response = CreateResponse("""{"reasoning_content":"alt thinking","content":"ok"}""");

        var events = ConversationEventFactory.FromResponse(response, 1000);

        Assert.Equal(2, events.Count);
        Assert.Equal("alt thinking", events[0].Data!.Value.GetProperty("content").GetString());
    }

    [Fact]
    public void FromResponse_WithEmptyReasoning_SkipsThinkingEvent()
    {
        var response = CreateResponse("""{"reasoning":"","content":"ok"}""");

        var events = ConversationEventFactory.FromResponse(response, 1000);

        Assert.Single(events);
        Assert.Equal(ConversationEventKind.Response, events[0].Kind);
    }

    [Fact]
    public void FromResponse_WithToolCalls_BuildsToolCallEventsBeforeResponse()
    {
        var response = CreateResponse(
            """
            {"content":null,"finish_reason":"tool_calls","tool_calls":[
              {"id":"call_1","type":"function","function":{"name":"get_weather","arguments":"{\"city\":\"Berlin\"}"}},
              {"id":"call_2","type":"function","function":{"name":"get_time","arguments":"{}"}}
            ]}
            """);

        var events = ConversationEventFactory.FromResponse(response, 1000);

        Assert.Equal(3, events.Count);
        Assert.Equal(ConversationEventKind.ToolCall, events[0].Kind);
        Assert.Equal(ConversationEventKind.ToolCall, events[1].Kind);
        Assert.Equal(ConversationEventKind.Response, events[2].Kind);

        var data = events[0].Data!.Value;
        Assert.Equal("call_1", data.GetProperty("id").GetString());
        Assert.Equal("get_weather", data.GetProperty("name").GetString());
        Assert.Equal("""{"city":"Berlin"}""", data.GetProperty("arguments").GetString());
    }

    [Fact]
    public void FromResponse_WithoutAssistantMessage_BuildsEmptyResponseEvent()
    {
        var response = new ConversationResponse("conv-1", "req-1", _now, "provider-a", "gpt-test", "/v1/chat/completions", null, null, false);

        var events = ConversationEventFactory.FromResponse(response, 1000);

        var data = Assert.Single(events).Data!.Value;
        Assert.Equal(string.Empty, data.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("finishReason").ValueKind);
        Assert.False(data.GetProperty("streamed").GetBoolean());
        Assert.False(data.TryGetProperty("usage", out _));
    }

    [Fact]
    public void FromFailure_BuildsErrorEvent()
    {
        var failure = new ConversationFailure("conv-1", "req-1", _now, "provider-a", "gpt-test", "/v1/chat/completions", 502, "bad gateway");

        var events = ConversationEventFactory.FromFailure(failure, 1000);

        var evt = Assert.Single(events);
        Assert.Equal(ConversationEventKind.Error, evt.Kind);
        Assert.Equal(502, evt.Data!.Value.GetProperty("statusCode").GetInt32());
        Assert.Equal("bad gateway", evt.Data!.Value.GetProperty("body").GetString());
    }

    [Fact]
    public void FromFailure_WithoutStatusCode_WritesNull()
    {
        var failure = new ConversationFailure("conv-1", "req-1", _now, null, null, null, null, null);

        var events = ConversationEventFactory.FromFailure(failure, 1000);

        var data = Assert.Single(events).Data!.Value;
        Assert.Equal(JsonValueKind.Null, data.GetProperty("statusCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("body").ValueKind);
    }

    [Fact]
    public void FromResponse_TruncatesOversizedStringField()
    {
        const int cap = 40;
        var response = CreateResponse($$"""{"reasoning":"{{new string('x', 200)}}","content":"ok"}""");

        var events = ConversationEventFactory.FromResponse(response, cap);

        var thinking = events[0].Data!.Value.GetProperty("content").GetString();
        Assert.Equal(cap, thinking!.Length);
        Assert.EndsWith(ConversationEventFactory.TruncatedSuffix, thinking, StringComparison.Ordinal);
    }

    [Fact]
    public void FromRequest_TruncatesStringsNestedInBody()
    {
        const int cap = 32;
        var request = CreateRequest($$"""{"messages":[{"role":"user","content":"{{new string('y', 200)}}"}]}""");

        var events = ConversationEventFactory.FromRequest(request, cap);

        var content = events[0].Data!.Value.GetProperty("body").GetProperty("messages")[0].GetProperty("content").GetString();
        Assert.Equal(cap, content!.Length);
        Assert.EndsWith(ConversationEventFactory.TruncatedSuffix, content, StringComparison.Ordinal);
    }

    [Fact]
    public void FromRequest_NonObjectBody_StillProducesRequestEvent()
    {
        var request = CreateRequest("""["not","an","object"]""");

        var events = ConversationEventFactory.FromRequest(request, 1000);

        var evt = Assert.Single(events);
        Assert.Equal(ConversationEventKind.Request, evt.Kind);
        Assert.Equal(0, evt.Data!.Value.GetProperty("messageCount").GetInt32());
    }

    [Fact]
    public void FromRequest_MalformedToolMessages_AreSkipped()
    {
        var request = CreateRequest("""{"messages":["a string",{"content":"no role"},{"role":"tool","tool_call_id":42,"content":"x"}]}""");

        var events = ConversationEventFactory.FromRequest(request, 1000);

        var toolResult = Assert.Single(events, e => e.Kind == ConversationEventKind.ToolResult);
        Assert.Equal(JsonValueKind.Null, toolResult.Data!.Value.GetProperty("toolCallId").ValueKind);
        Assert.Equal("x", toolResult.Data!.Value.GetProperty("content").GetString());
    }

    [Fact]
    public void FromResponse_OddlyTypedToolCalls_AreSkippedButResponseKept()
    {
        var response = CreateResponse("""{"content":"ok","tool_calls":"not-an-array"}""");

        var events = ConversationEventFactory.FromResponse(response, 1000);

        Assert.Single(events);
        Assert.Equal(ConversationEventKind.Response, events[0].Kind);
    }

    [Fact]
    public void FromResponse_ToolCallWithoutFunction_KeepsIdAndNullFields()
    {
        var response = CreateResponse("""{"content":"ok","tool_calls":[{"id":"call_1"}]}""");

        var events = ConversationEventFactory.FromResponse(response, 1000);

        var data = events[0].Data!.Value;
        Assert.Equal("call_1", data.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("name").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("arguments").ValueKind);
    }

    private static ConversationRequest CreateRequest(string bodyJson) =>
        new("conv-1", "req-1", _now, "provider-a", "gpt-test", "/v1/chat/completions", Json(bodyJson));

    private static ConversationResponse CreateResponse(string assistantJson, string? usageJson = null) =>
        new(
            "conv-1",
            "req-1",
            _now,
            "provider-a",
            "gpt-test",
            "/v1/chat/completions",
            Json(assistantJson),
            usageJson is null ? null : Json(usageJson),
            true);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
