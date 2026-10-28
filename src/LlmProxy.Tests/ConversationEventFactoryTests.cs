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

        var events = ConversationEventFactory.FromRequest(request, RequestDedupeContext.Empty, 1000);

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

        var events = ConversationEventFactory.FromRequest(request, RequestDedupeContext.Empty, 1000);

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

        var events = ConversationEventFactory.FromRequest(request, RequestDedupeContext.Empty, 1000);

        var evt = Assert.Single(events);
        Assert.Equal(0, evt.Data!.Value.GetProperty("messageCount").GetInt32());
    }

    [Fact]
    public void FromRequest_NothingRecordedYet_StoresEveryMessageOfTheRequest()
    {
        var request = CreateRequest("""{"messages":[{"role":"user","content":"hi"},{"role":"assistant","content":"hello"}]}""");

        var data = Assert.Single(ConversationEventFactory.FromRequest(request, RequestDedupeContext.Empty, 1000)).Data!.Value;

        Assert.Equal(2, data.GetProperty("messageCount").GetInt32());
        Assert.Equal(0, data.GetProperty("recordedCount").GetInt32());
        Assert.Equal(2, data.GetProperty("newMessageCount").GetInt32());
        Assert.Equal(2, data.GetProperty("messages").GetArrayLength());
        Assert.Equal("hi", data.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Matches("^[0-9a-f]{16}$", data.GetProperty("lastMessageFingerprint").GetString()!);
        Assert.False(data.TryGetProperty("toolsFingerprint", out _));
    }

    [Fact]
    public void FromRequest_ResentHistory_StoresOnlyTheMessagesThatAreNew()
    {
        var first = CreateRequest("""{"model":"gpt-test","messages":[{"role":"user","content":"one"},{"role":"assistant","content":"two"}]}""");
        var firstData = Assert.Single(ConversationEventFactory.FromRequest(first, RequestDedupeContext.Empty, 1000)).Data!.Value;

        var resent = CreateRequest(
            """
            {"model":"gpt-test","messages":[
              {"role":"user","content":"one"},
              {"role":"assistant","content":"two"},
              {"role":"user","content":"three"}
            ]}
            """);

        var context = new RequestDedupeContext(2, firstData.GetProperty("lastMessageFingerprint").GetString(), null);
        var data = Assert.Single(ConversationEventFactory.FromRequest(resent, context, 1000)).Data!.Value;

        Assert.Equal(3, data.GetProperty("messageCount").GetInt32());
        Assert.Equal(2, data.GetProperty("recordedCount").GetInt32());
        Assert.Equal(1, data.GetProperty("newMessageCount").GetInt32());

        var stored = data.GetProperty("messages");
        Assert.Equal(1, stored.GetArrayLength());
        Assert.Equal("three", stored[0].GetProperty("content").GetString());

        // The body keeps every other request field but no longer repeats the messages, which live in data.messages.
        var body = data.GetProperty("body");
        Assert.Equal("gpt-test", body.GetProperty("model").GetString());
        Assert.False(body.TryGetProperty("messages", out _));
    }

    [Fact]
    public void FromRequest_EmitsToolResultsOnlyForMessagesThatAreNew()
    {
        var first = CreateRequest(
            """
            {"messages":[
              {"role":"user","content":"weather?"},
              {"role":"tool","tool_call_id":"call_1","name":"get_weather","content":"sunny"}
            ]}
            """);

        var firstEvents = ConversationEventFactory.FromRequest(first, RequestDedupeContext.Empty, 1000);
        var recorded = firstEvents[0].Data!.Value.GetProperty("lastMessageFingerprint").GetString();
        Assert.Single(firstEvents, evt => evt.Kind == ConversationEventKind.ToolResult);

        var resent = CreateRequest(
            """
            {"messages":[
              {"role":"user","content":"weather?"},
              {"role":"tool","tool_call_id":"call_1","name":"get_weather","content":"sunny"},
              {"role":"assistant","content":"It is sunny."},
              {"role":"user","content":"and tomorrow?"},
              {"role":"tool","tool_call_id":"call_2","name":"get_weather","content":"rain"}
            ]}
            """);

        var events = ConversationEventFactory.FromRequest(resent, new RequestDedupeContext(2, recorded, null), 1000);

        var toolResult = Assert.Single(events, evt => evt.Kind == ConversationEventKind.ToolResult);
        Assert.Equal("call_2", toolResult.Data!.Value.GetProperty("toolCallId").GetString());
        Assert.Equal("rain", toolResult.Data!.Value.GetProperty("content").GetString());
    }

    [Fact]
    public void FromRequest_RewrittenHistory_StoresEverythingRatherThanDropMessages()
    {
        var request = CreateRequest(
            """
            {"messages":[
              {"role":"user","content":"one"},
              {"role":"assistant","content":"two, edited by the client"},
              {"role":"user","content":"three"}
            ]}
            """);

        var data = Assert.Single(
            ConversationEventFactory.FromRequest(request, new RequestDedupeContext(2, "0000000000000000", null), 1000)).Data!.Value;

        Assert.Equal(3, data.GetProperty("messageCount").GetInt32());
        Assert.Equal(0, data.GetProperty("recordedCount").GetInt32());
        Assert.Equal(3, data.GetProperty("newMessageCount").GetInt32());
        Assert.Equal(3, data.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public void FromRequest_RecordedCountLargerThanTheResentHistory_StoresEverything()
    {
        var request = CreateRequest("""{"messages":[{"role":"user","content":"one"}]}""");

        var data = Assert.Single(
            ConversationEventFactory.FromRequest(request, new RequestDedupeContext(5, "0000000000000000", null), 1000)).Data!.Value;

        Assert.Equal(0, data.GetProperty("recordedCount").GetInt32());
        Assert.Equal(1, data.GetProperty("newMessageCount").GetInt32());
    }

    [Fact]
    public void FromRequest_UnchangedTools_AreOmittedAndCarryNoFingerprint()
    {
        const string Body =
            """
            {"model":"gpt-test",
             "tools":[{"type":"function","function":{"name":"get_weather","arguments":{"type":"object"}}}],
             "messages":[{"role":"user","content":"hi"}]}
            """;

        var first = Assert.Single(ConversationEventFactory.FromRequest(CreateRequest(Body), RequestDedupeContext.Empty, 1000)).Data!.Value;
        var toolsFingerprint = first.GetProperty("toolsFingerprint").GetString();
        Assert.Equal(1, first.GetProperty("body").GetProperty("tools").GetArrayLength());

        var context = new RequestDedupeContext(1, first.GetProperty("lastMessageFingerprint").GetString(), toolsFingerprint);
        var second = Assert.Single(ConversationEventFactory.FromRequest(CreateRequest(Body), context, 1000)).Data!.Value;

        Assert.False(second.TryGetProperty("toolsFingerprint", out _));
        Assert.False(second.GetProperty("body").TryGetProperty("tools", out _));
        Assert.Equal("gpt-test", second.GetProperty("body").GetProperty("model").GetString());
    }

    [Fact]
    public void FromRequest_ChangedTools_AreStoredWithANewFingerprint()
    {
        var first = Assert.Single(
            ConversationEventFactory.FromRequest(
                CreateRequest("""{"tools":[{"type":"function","function":{"name":"get_weather"}}],"messages":[{"role":"user","content":"hi"}]}"""),
                RequestDedupeContext.Empty,
                1000)).Data!.Value;

        var context = new RequestDedupeContext(1, first.GetProperty("lastMessageFingerprint").GetString(), first.GetProperty("toolsFingerprint").GetString());
        var second = Assert.Single(
            ConversationEventFactory.FromRequest(
                CreateRequest("""{"tools":[{"type":"function","function":{"name":"get_time"}}],"messages":[{"role":"user","content":"hi"}]}"""),
                context,
                1000)).Data!.Value;

        Assert.NotEqual(first.GetProperty("toolsFingerprint").GetString(), second.GetProperty("toolsFingerprint").GetString());
        Assert.Equal(
            "get_time",
            second.GetProperty("body").GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
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

    /// <summary>
    /// A cap smaller than the "...[truncated]" suffix used to return the bare suffix, i.e. a stored field longer
    /// than the cap it was supposed to enforce.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(13)]
    [InlineData(14)]
    public void FromResponse_CapSmallerThanTheTruncationSuffix_StillRespectsTheCap(int cap)
    {
        var response = CreateResponse($$"""{"reasoning":"{{new string('x', 200)}}","content":"ok"}""");

        var events = ConversationEventFactory.FromResponse(response, cap);

        var thinking = events[0].Data!.Value.GetProperty("content").GetString();
        Assert.Equal(cap, thinking!.Length);
    }

    [Fact]
    public void FromRequest_CapSmallerThanTheTruncationSuffix_StillRespectsTheCap()
    {
        const int cap = 10;
        var request = CreateRequest($$"""{"messages":[{"role":"user","content":"{{new string('y', 200)}}"}]}""");

        var events = ConversationEventFactory.FromRequest(request, RequestDedupeContext.Empty, cap);

        var content = events[0].Data!.Value.GetProperty("messages")[0].GetProperty("content").GetString();
        Assert.Equal(cap, content!.Length);
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

        var events = ConversationEventFactory.FromRequest(request, RequestDedupeContext.Empty, cap);

        var data = events[0].Data!.Value;
        var content = data.GetProperty("messages")[0].GetProperty("content").GetString();
        Assert.Equal(cap, content!.Length);
        Assert.EndsWith(ConversationEventFactory.TruncatedSuffix, content, StringComparison.Ordinal);
        Assert.False(data.GetProperty("body").TryGetProperty("messages", out _));
    }

    [Fact]
    public void FromRequest_NonObjectBody_StillProducesRequestEvent()
    {
        var request = CreateRequest("""["not","an","object"]""");

        var events = ConversationEventFactory.FromRequest(request, RequestDedupeContext.Empty, 1000);

        var evt = Assert.Single(events);
        Assert.Equal(ConversationEventKind.Request, evt.Kind);
        Assert.Equal(0, evt.Data!.Value.GetProperty("messageCount").GetInt32());
    }

    [Fact]
    public void FromRequest_MalformedToolMessages_AreSkipped()
    {
        var request = CreateRequest("""{"messages":["a string",{"content":"no role"},{"role":"tool","tool_call_id":42,"content":"x"}]}""");

        var events = ConversationEventFactory.FromRequest(request, RequestDedupeContext.Empty, 1000);

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
