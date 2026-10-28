namespace LlmProxy.Application.Services;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmProxy.Application.Logging;

/// <summary>
/// Folds the <c>data:</c> chunks of an OpenAI streaming response into one assistant message plus usage.
/// </summary>
/// <remarks>
/// A streamed assistant message arrives spread over many chunks: content and reasoning as fragments and tool
/// calls split per <c>index</c>. <see cref="Append"/> is total: anything it cannot understand is skipped, never thrown.
/// </remarks>
public sealed class SseAssistantAssembler
{
    private const string DoneSentinel = "[DONE]";

    private readonly ILogger<SseAssistantAssembler>? _logger;
    private readonly StringBuilder _content = new();
    private readonly StringBuilder _reasoning = new();
    private readonly SortedDictionary<int, JsonObject> _toolCalls = new();
    private readonly Dictionary<int, StringBuilder> _toolArguments = new();

    private string? _finishReason;
    private JsonObject? _usage;

    /// <summary>Creates an assembler.</summary>
    /// <param name="logger">Optional logger used to report chunks that had to be skipped.</param>
    public SseAssistantAssembler(ILogger<SseAssistantAssembler>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>True when any content, reasoning or tool call fragment has been received.</summary>
    public bool HasContent => _content.Length > 0 || _reasoning.Length > 0 || _toolCalls.Count > 0;

    /// <summary>The assembled assistant message, or null when the stream carried nothing.</summary>
    public JsonElement? AssistantMessage => BuildAssistant() is { } message ? ToElement(message) : null;

    /// <summary>The usage object the stream reported, or null when no chunk carried one.</summary>
    public JsonElement? Usage => _usage is { } usage ? ToElement(usage) : null;

    /// <summary>
    /// Folds one server-sent event payload into the message being assembled.
    /// </summary>
    /// <param name="dataPayload">The text after <c>data: </c>, excluding the <c>[DONE]</c> sentinel.</param>
    public void Append(string dataPayload)
    {
        var payload = dataPayload?.Trim();
        if (string.IsNullOrEmpty(payload) || payload == DoneSentinel)
        {
            return;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(payload);
        }
        catch (JsonException ex)
        {
            _logger?.SseChunkSkipped("payload is not valid JSON", ex);
            return;
        }

        if (node is not JsonObject chunk)
        {
            _logger?.SseChunkSkipped("payload is not a JSON object");
            return;
        }

        if (chunk["usage"] is JsonObject usage)
        {
            _usage = (JsonObject)usage.DeepClone();
        }

        if (chunk["choices"] is not JsonArray { Count: > 0 } choices || choices[0] is not JsonObject choice)
        {
            return;
        }

        if (GetString(choice, "finish_reason") is { Length: > 0 } finishReason)
        {
            _finishReason = finishReason;
        }

        if (choice["delta"] is not JsonObject delta)
        {
            return;
        }

        AppendTo(_content, GetString(delta, "content"));
        // Providers spell reasoning differently; both land in the single "reasoning" field.
        AppendTo(_reasoning, GetString(delta, "reasoning"));
        AppendTo(_reasoning, GetString(delta, "reasoning_content"));
        MergeToolCalls(delta["tool_calls"] as JsonArray);
    }

    private void MergeToolCalls(JsonArray? calls)
    {
        if (calls is null)
        {
            return;
        }

        for (var position = 0; position < calls.Count; position++)
        {
            if (calls[position] is not JsonObject call)
            {
                continue;
            }

            var index = call["index"] is JsonValue value && value.TryGetValue<int>(out var explicitIndex)
                ? explicitIndex
                : position;

            if (!_toolCalls.TryGetValue(index, out var merged))
            {
                merged = new JsonObject();
                _toolCalls[index] = merged;
            }

            SetIfMissing(merged, "id", GetString(call, "id"));
            SetIfMissing(merged, "type", GetString(call, "type"));

            if (call["function"] is not JsonObject function)
            {
                continue;
            }

            if (GetString(function, "name") is { } name)
            {
                var mergedFunction = merged["function"] as JsonObject ?? new JsonObject();
                SetIfMissing(mergedFunction, "name", name);
                merged["function"] = mergedFunction;
            }

            if (GetString(function, "arguments") is { } arguments)
            {
                if (!_toolArguments.TryGetValue(index, out var buffer))
                {
                    buffer = new StringBuilder();
                    _toolArguments[index] = buffer;
                }

                buffer.Append(arguments);
            }
        }
    }

    private JsonObject? BuildAssistant()
    {
        if (!HasContent && _finishReason is null)
        {
            return null;
        }

        var message = new JsonObject { ["role"] = "assistant" };

        if (_content.Length > 0)
        {
            message["content"] = _content.ToString();
        }

        if (_reasoning.Length > 0)
        {
            message["reasoning"] = _reasoning.ToString();
        }

        if (_toolCalls.Count > 0)
        {
            var calls = new JsonArray();
            foreach (var (index, call) in _toolCalls)
            {
                var merged = (JsonObject)call.DeepClone();

                if (_toolArguments.TryGetValue(index, out var arguments) && arguments.Length > 0)
                {
                    var function = merged["function"] as JsonObject ?? new JsonObject();
                    function["arguments"] = arguments.ToString();
                    merged["function"] = function;
                }

                calls.Add(merged);
            }

            message["tool_calls"] = calls;
        }

        if (_finishReason is not null)
        {
            message["finish_reason"] = _finishReason;
        }

        return message;
    }

    private static void AppendTo(StringBuilder target, string? value)
    {
        if (value is not null)
        {
            target.Append(value);
        }
    }

    private static void SetIfMissing(JsonObject target, string key, string? value)
    {
        if (value is not null && string.IsNullOrEmpty(GetString(target, key)))
        {
            target[key] = value;
        }
    }

    private static string? GetString(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static JsonElement ToElement(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }
}
