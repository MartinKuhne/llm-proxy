namespace LlmProxy.Application.Services;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmProxy.Application.Logging;
using LlmProxy.Domain;

/// <summary>
/// Turns the incoming request and a buffered upstream response into the payloads stored in the conversation audit log.
/// </summary>
/// <remarks>
/// Capture never consumes the request: the body stream is always rewound so forwarding is unaffected, and a body larger
/// than <see cref="MaxAuditBodyBytes"/> is stored truncated and flagged.
/// </remarks>
public sealed class AuditBodyCapture
{
    /// <summary>Largest request body copy kept for the audit log; bigger uploads are recorded truncated.</summary>
    internal const int MaxAuditBodyBytes = 2_000_000;

    private const int AuditReadChunkBytes = 64 * 1024;

    private readonly ILogger<AuditBodyCapture> _logger;

    public AuditBodyCapture(ILogger<AuditBodyCapture> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Captures a JSON copy of the request body for the audit log without consuming it: the stream is always
    /// rewound so forwarding is unaffected.
    /// </summary>
    /// <param name="request">Incoming request, already buffered.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A JSON element describing the body: the parsed body, a multipart summary or the raw text.</returns>
    public async Task<JsonElement> CaptureRequestAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        if (ProxyService.IsMultipartRequest(request))
        {
            return ToElement(BuildMultipartAuditBody(request));
        }

        var (text, truncated) = await ReadAuditTextAsync(request, cancellationToken);

        JsonNode audit;
        if (ProxyService.IsJsonRequest(request) && TryParseJson(text, out var parsed) && parsed is not null)
        {
            audit = parsed;
        }
        else
        {
            audit = new JsonObject { ["raw"] = text };
        }

        if (truncated)
        {
            _logger.AuditBodyTruncated(MaxAuditBodyBytes);

            if (audit is JsonObject auditObject)
            {
                auditObject["auditTruncated"] = true;
            }
        }

        return ToElement(audit);
    }

    /// <summary>Model the client asked for, read from the audit body (multipart requests carry it in the fields).</summary>
    /// <param name="body">Captured request body.</param>
    public string? RequestedModel(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (body.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String)
        {
            return model.GetString();
        }

        return body.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object &&
               fields.TryGetProperty("model", out var fieldModel) && fieldModel.ValueKind == JsonValueKind.String
            ? fieldModel.GetString()
            : null;
    }

    /// <summary>
    /// Builds the audit response for a buffered (non-streamed) upstream response.
    /// </summary>
    /// <param name="responseBytes">Body the upstream returned.</param>
    /// <param name="provider">Provider that served the request.</param>
    /// <param name="path">Request path.</param>
    /// <param name="conversationId">Conversation the response belongs to.</param>
    /// <param name="requestId">Request that produced the response.</param>
    public ConversationResponse BuildBufferedResponse(
        byte[] responseBytes,
        ProviderConfig provider,
        string path,
        string conversationId,
        string requestId)
    {
        JsonElement? assistant = null;
        JsonElement? usage = null;
        JsonElement? raw = null;

        try
        {
            using var document = JsonDocument.Parse(responseBytes);
            var body = document.RootElement;

            if (body.ValueKind == JsonValueKind.Object)
            {
                if (body.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
                {
                    usage = usageElement.Clone();
                }

                assistant = FirstAssistantMessage(body);
            }

            // Endpoints without an assistant message (embeddings and friends) keep their whole body as raw JSON.
            if (assistant is null && body.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                raw = body.Clone();
            }
        }
        catch (JsonException)
        {
            // An unparseable upstream body simply leaves the response event without payload; the client still got it.
        }

        return new ConversationResponse(
            conversationId, requestId, DateTimeOffset.UtcNow, provider.Name, provider.Model, path,
            assistant, usage, Streamed: false, raw);
    }

    /// <summary>
    /// Reads at most <see cref="MaxAuditBodyBytes"/> of the request body as text, then rewinds it.
    /// </summary>
    private static async Task<(string Text, bool Truncated)> ReadAuditTextAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        request.Body.Position = 0;

        var buffer = new byte[AuditReadChunkBytes];
        using var captured = new MemoryStream();
        var truncated = false;

        try
        {
            while (true)
            {
                var read = await request.Body.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                var room = MaxAuditBodyBytes - (int)captured.Length;
                if (read > room)
                {
                    captured.Write(buffer, 0, Math.Max(room, 0));
                    truncated = true;
                    break;
                }

                captured.Write(buffer, 0, read);
            }

            return (Encoding.UTF8.GetString(captured.ToArray()), truncated);
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    /// <summary>
    /// Summarises a multipart request (audio transcription and friends) without recording binary content.
    /// </summary>
    private JsonObject BuildMultipartAuditBody(HttpRequest request)
    {
        var audit = new JsonObject
        {
            ["multipart"] = true,
            ["contentType"] = request.ContentType ?? string.Empty,
        };

        try
        {
            var fields = new JsonObject();
            foreach (var field in request.Form)
            {
                fields[field.Key] = field.Value.ToString();
            }

            var files = new JsonArray();
            foreach (var file in request.Form.Files)
            {
                files.Add(new JsonObject
                {
                    ["name"] = file.Name,
                    ["fileName"] = file.FileName,
                    ["contentType"] = file.ContentType,
                    ["size"] = file.Length,
                });
            }

            audit["fields"] = fields;
            audit["files"] = files;
        }
        catch (Exception ex)
        {
            // A form the server cannot read must not stop the request; the content type alone is still useful.
            _logger.AuditBodyUnavailable(ex.Message);
        }

        return audit;
    }

    private static JsonElement? FirstAssistantMessage(JsonElement body)
    {
        if (body.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.ValueKind == JsonValueKind.Object &&
                    choice.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
                {
                    return message.Clone();
                }
            }
        }

        return null;
    }

    private static bool TryParseJson(string? text, out JsonNode? node)
    {
        node = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            node = JsonNode.Parse(text);
            return node is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonElement ToElement(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }
}
