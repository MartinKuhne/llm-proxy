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
    /// Builds the request event plus one tool-result event for every <c>role=tool</c> message in the request body.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="maxFieldChars">Largest size kept for any single string field.</param>
    public static IReadOnlyList<ConversationEvent> FromRequest(ConversationRequest request, int maxFieldChars)
    {
        var cap = Cap(maxFieldChars);

        ConversationEvent Create(string kind, JsonObject data) => new(
            kind, request.Timestamp, request.ConversationId, request.RequestId,
            request.Provider, request.Model, request.Path, ToElement(data));

        var events = new List<ConversationEvent>();
        var messages = TryGetArray(request.Body, "messages");

        events.Add(Create(ConversationEventKind.Request, new JsonObject
        {
            ["body"] = ToNode(request.Body, cap),
            ["messageCount"] = messages?.GetArrayLength() ?? 0,
        }));

        if (messages is null)
        {
            return events;
        }

        foreach (var message in messages.Value.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object || TryGetString(message, "role") != "tool")
            {
                continue;
            }

            var data = new JsonObject
            {
                ["toolCallId"] = TryGetString(message, "tool_call_id"),
            };

            if (message.TryGetProperty("name", out var name))
            {
                data["name"] = ToNode(name, cap);
            }

            data["content"] = message.TryGetProperty("content", out var content) ? ToNode(content, cap) : null;

            events.Add(Create(ConversationEventKind.ToolResult, data));
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
