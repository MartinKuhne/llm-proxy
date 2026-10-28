namespace LlmProxy.Application.Services;

using System.Text.Json;
using System.Text.Json.Nodes;
using LlmProxy.Domain;

/// <summary>
/// Derives the audit events stored for a conversation from the request, response and failure inputs reported by the proxy.
/// </summary>
/// <remarks>
/// JSON access is deliberately defensive: properties that are missing or oddly typed are skipped so a malformed
/// upstream payload can never throw into the caller.
/// </remarks>
internal static class ConversationEventFactory
{
    internal const string TruncatedSuffix = "...[truncated]";

    /// <summary>
    /// Builds the request event plus one tool-result event for every <c>role=tool</c> message the client added
    /// since the previous turn.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="dedupe">What this conversation already has stored, so resent content is not written twice.</param>
    /// <param name="maxFieldChars">Largest size kept for any single string field.</param>
    /// <remarks>
    /// Clients resend the whole history every turn. Only the messages the log does not hold yet are stored, in the
    /// event's own <c>messages</c> field; the stored <c>body</c> keeps the remaining request fields with the
    /// resent <c>messages</c> and an unchanged <c>tools</c> array removed, so nothing is written twice. When the
    /// stored history does not line up with the resent one — a rewritten or branched conversation — the whole history
    /// is stored instead, so nothing is ever silently dropped.
    /// </remarks>
    public static IReadOnlyList<ConversationEvent> FromRequest(
        ConversationRequest request,
        RequestDedupeContext dedupe,
        int maxFieldChars)
    {
        var cap = Cap(maxFieldChars);

        ConversationEvent Create(string kind, JsonObject data) => new(
            kind, request.Timestamp, request.ConversationId, request.RequestId,
            request.Provider, request.Model, request.Path, ToElement(data));

        var events = new List<ConversationEvent>();
        var messages = TryGetArray(request.Body, "messages");
        var skip = SkipCount(messages, dedupe);
        var fresh = NewMessages(messages, skip);
        var toolsFingerprint = JsonFingerprint.Of(TryGetArray(request.Body, "tools") ?? default);
        var toolsAlreadyStored = toolsFingerprint is not null && toolsFingerprint == dedupe.RecordedToolsFingerprint;

        var data = new JsonObject
        {
            ["body"] = BuildBody(request.Body, hasMessages: messages is not null, toolsAlreadyStored, cap),
            ["messages"] = ToArrayNode(fresh, cap),
            ["messageCount"] = messages?.GetArrayLength() ?? 0,
            ["recordedCount"] = skip,
            ["newMessageCount"] = fresh.Count,
            ["lastMessageFingerprint"] = fresh.Count > 0 ? JsonFingerprint.Of(fresh[^1]) : null,
        };

        if (toolsFingerprint is not null && !toolsAlreadyStored)
        {
            data["toolsFingerprint"] = toolsFingerprint;
        }

        events.Add(Create(ConversationEventKind.Request, data));

        foreach (var message in fresh)
        {
            if (message.ValueKind != JsonValueKind.Object || TryGetString(message, "role") != "tool")
            {
                continue;
            }

            var toolResult = new JsonObject
            {
                ["toolCallId"] = TryGetString(message, "tool_call_id"),
            };

            if (message.TryGetProperty("name", out var name))
            {
                toolResult["name"] = ToNode(name, cap);
            }

            toolResult["content"] = message.TryGetProperty("content", out var content) ? ToNode(content, cap) : null;

            events.Add(Create(ConversationEventKind.ToolResult, toolResult));
        }

        return events;
    }

    /// <summary>
    /// Builds the thinking event (when the model reported reasoning), one tool-call event per requested tool call,
    /// and the response event, in that order.
    /// </summary>
    /// <param name="response">The completed response.</param>
    /// <param name="maxFieldChars">Largest size kept for any single string field.</param>
    public static IReadOnlyList<ConversationEvent> FromResponse(ConversationResponse response, int maxFieldChars)
    {
        var cap = Cap(maxFieldChars);

        ConversationEvent Create(string kind, JsonObject data) => new(
            kind, response.Timestamp, response.ConversationId, response.RequestId,
            response.Provider, response.Model, response.Path, ToElement(data));

        var events = new List<ConversationEvent>();
        var assistant = response.AssistantMessage;
        var hasAssistant = assistant?.ValueKind == JsonValueKind.Object;

        if (hasAssistant)
        {
            var message = assistant!.Value;

            var reasoning = TryGetString(message, "reasoning") ?? TryGetString(message, "reasoning_content");
            if (!string.IsNullOrEmpty(reasoning))
            {
                events.Add(Create(ConversationEventKind.Thinking, new JsonObject { ["content"] = Truncate(reasoning, cap) }));
            }

            foreach (var call in EnumerateToolCalls(message))
            {
                JsonElement? function = null;
                if (call.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object)
                {
                    function = fn;
                }

                events.Add(Create(ConversationEventKind.ToolCall, new JsonObject
                {
                    ["id"] = TryGetString(call, "id"),
                    ["name"] = function.HasValue ? TryGetString(function.Value, "name") : null,
                    ["arguments"] = function.HasValue && function.Value.TryGetProperty("arguments", out var args) ? ToNode(args, cap) : null,
                }));
            }
        }

        var data = new JsonObject
        {
            ["content"] = AssistantContent(assistant, cap),
            ["finishReason"] = hasAssistant ? TryGetString(assistant!.Value, "finish_reason") : null,
            ["streamed"] = response.Streamed,
        };

        if (response.Usage is { } usage && usage.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            data["usage"] = ToNode(usage, cap);
        }

        // Non-chat endpoints (embeddings and friends) have no assistant message; keep their body as raw JSON.
        if (!hasAssistant && response.RawResponse is { } raw && raw.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            data["raw"] = ToNode(raw, cap);
        }

        events.Add(Create(ConversationEventKind.Response, data));

        return events;
    }

    /// <summary>
    /// Builds the single error event describing an upstream failure.
    /// </summary>
    /// <param name="failure">The failure to record.</param>
    /// <param name="maxFieldChars">Largest size kept for any single string field.</param>
    public static IReadOnlyList<ConversationEvent> FromFailure(ConversationFailure failure, int maxFieldChars)
    {
        var cap = Cap(maxFieldChars);

        var data = new JsonObject
        {
            ["statusCode"] = failure.StatusCode,
            ["body"] = Truncate(failure.Body, cap),
        };

        return
        [
            new ConversationEvent(
                ConversationEventKind.Error, failure.Timestamp, failure.ConversationId, failure.RequestId,
                failure.Provider, failure.Model, failure.Path, ToElement(data))
        ];
    }

    /// <summary>
    /// Number of leading request messages already on disk: the recorded count when the message stored at that index
    /// still matches its fingerprint, zero whenever the client rewrote or branched its history.
    /// </summary>
    private static int SkipCount(JsonElement? messages, RequestDedupeContext dedupe)
    {
        if (messages is null || dedupe.RecordedMessageCount <= 0 || dedupe.LastRecordedMessageFingerprint is null)
        {
            return 0;
        }

        var total = messages.Value.GetArrayLength();
        if (dedupe.RecordedMessageCount > total)
        {
            return 0;
        }

        var anchor = JsonFingerprint.Of(messages.Value[dedupe.RecordedMessageCount - 1]);

        return anchor is not null && anchor == dedupe.LastRecordedMessageFingerprint ? dedupe.RecordedMessageCount : 0;
    }

    private static IReadOnlyList<JsonElement> NewMessages(JsonElement? messages, int skip)
    {
        if (messages is null)
        {
            return [];
        }

        var fresh = new List<JsonElement>();
        var index = 0;
        foreach (var message in messages.Value.EnumerateArray())
        {
            if (index++ >= skip)
            {
                fresh.Add(message);
            }
        }

        return fresh;
    }

    /// <summary>
    /// Copy of the request body with the resent <c>messages</c> and already-recorded <c>tools</c> removed; every
    /// other request field is kept as it arrived. The messages themselves live in the event's own
    /// <c>messages</c> field, so storing them here too would write the same content twice.
    /// </summary>
    private static JsonNode? BuildBody(JsonElement body, bool hasMessages, bool omitTools, int cap)
    {
        if (body.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        var raw = body.GetRawText();
        var node = Parse(raw);

        if (node is JsonObject obj)
        {
            if (hasMessages)
            {
                obj.Remove("messages");
            }

            if (omitTools)
            {
                obj.Remove("tools");
            }
        }

        return raw.Length > cap ? TruncateNode(node, cap) : node;
    }

    private static JsonArray ToArrayNode(IReadOnlyList<JsonElement> elements, int cap)
    {
        var array = new JsonArray();
        foreach (var element in elements)
        {
            array.Add(ToNode(element, cap));
        }

        return array;
    }

    private static JsonNode? Parse(string raw)
    {
        try
        {
            return JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<JsonElement> EnumerateToolCalls(JsonElement message)
    {
        var calls = TryGetArray(message, "tool_calls");
        if (calls is null)
        {
            yield break;
        }

        foreach (var call in calls.Value.EnumerateArray())
        {
            if (call.ValueKind == JsonValueKind.Object)
            {
                yield return call;
            }
        }
    }

    private static JsonNode AssistantContent(JsonElement? assistant, int cap)
    {
        if (assistant is { } message && message.ValueKind == JsonValueKind.Object &&
            message.TryGetProperty("content", out var content) &&
            content.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            return ToNode(content, cap) ?? JsonValue.Create(string.Empty);
        }

        return JsonValue.Create(string.Empty);
    }

    private static JsonElement? ToElement(JsonObject data)
    {
        if (data.Count == 0)
        {
            return null;
        }

        using var document = JsonDocument.Parse(data.ToJsonString());
        return document.RootElement.Clone();
    }

    private static JsonNode? ToNode(JsonElement element, int cap)
    {
        if (element.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        var raw = element.GetRawText();

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            return null;
        }

        return raw.Length > cap ? TruncateNode(node, cap) : node;
    }

    private static JsonNode? TruncateNode(JsonNode? node, int cap)
    {
        switch (node)
        {
            case JsonObject obj:
                var properties = new JsonObject();
                foreach (var property in obj)
                {
                    properties[property.Key] = TruncateNode(property.Value, cap);
                }

                return properties;

            case JsonArray array:
                var items = new JsonArray();
                foreach (var item in array)
                {
                    items.Add(TruncateNode(item, cap));
                }

                return items;

            case JsonValue value when value.TryGetValue<string>(out var text) && text.Length > cap:
                return Truncate(text, cap);

            default:
                return node?.DeepClone();
        }
    }

    private static string? Truncate(string? value, int cap)
    {
        if (value is null || value.Length <= cap)
        {
            return value;
        }

        var keep = Math.Max(0, cap - TruncatedSuffix.Length);
        return string.Concat(value.AsSpan(0, keep), TruncatedSuffix);
    }

    private static int Cap(int maxFieldChars) => maxFieldChars > 0 ? maxFieldChars : int.MaxValue;

    private static JsonElement? TryGetArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array ? value : null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
