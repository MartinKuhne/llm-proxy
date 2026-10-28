namespace LlmProxy.Application.Services;

using LlmProxy.Application.Configuration;
using LlmProxy.Application.Logging;
using LlmProxy.Domain;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

public class ProxyService : IProxyService
{
    // Headers that must not be forwarded between hops
    private static readonly HashSet<string> _hopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Transfer-Encoding", "TE",
        "Trailers", "Upgrade", "Proxy-Authorization", "Proxy-Authenticate"
    };

    // Largest request body copy kept for the audit log; bigger uploads are recorded truncated.
    private const int MaxAuditBodyBytes = 2_000_000;
    private const int AuditReadChunkBytes = 64 * 1024;

    private readonly ILogger<ProxyService> _logger;
    private readonly IProviderManager _providerManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITokenCountLogger _tokenCountLogger;
    private readonly ProvidersConfig _providersConfig;
    private readonly IConversationLogger _conversationLogger;
    private readonly ConversationIdResolver _conversationIdResolver;
    private readonly ILoggerFactory _loggerFactory;

    public ProxyService(
        ILogger<ProxyService> logger,
        IProviderManager providerManager,
        IHttpClientFactory httpClientFactory,
        ITokenCountLogger tokenCountLogger,
        ProvidersConfig providersConfig,
        IConversationLogger conversationLogger,
        ConversationIdResolver conversationIdResolver,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _providerManager = providerManager;
        _httpClientFactory = httpClientFactory;
        _tokenCountLogger = tokenCountLogger;
        _providersConfig = providersConfig;
        _conversationLogger = conversationLogger;
        _conversationIdResolver = conversationIdResolver;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Proxies requests to upstream providers with automatic failover, recording the conversation audit trail on the way.
    /// </summary>
    public async Task ProxyAsync(HttpContext context, string path, CancellationToken cancellationToken = default)
    {
        // Buffer the request body so it can be re-read on retry
        context.Request.EnableBuffering();

        var auditBody = await CaptureAuditBodyAsync(context.Request, cancellationToken);
        var conversationId = _conversationIdResolver.Resolve(context.Request, auditBody);
        var requestId = Guid.NewGuid().ToString("N");

        var bestProvider = GetProviderForRequest(context);

        // Record the prompt before any provider is tried so it survives a crash mid-request.
        await _conversationLogger.LogRequestAsync(
            new ConversationRequest(
                conversationId,
                requestId,
                DateTimeOffset.UtcNow,
                bestProvider?.Name,
                RequestedModel(auditBody),
                path,
                auditBody),
            cancellationToken);

        if (bestProvider == null)
        {
            _logger.ProxyNoProviders();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        var providers = new[] { bestProvider }
            .Concat(_providerManager.GetAllProviders().Where(p => p != bestProvider && p.AllowAutomatic));

        foreach (var provider in providers)
        {
            if (context.Response.HasStarted)
                return;

            context.Request.Body.Position = 0;

            try
            {
                var (forwarded, statusCode, responseBody, response) = await TryForwardAsync(
                    context, provider, path, conversationId, requestId, cancellationToken);

                if (forwarded)
                {
                    if (response is not null)
                    {
                        await _conversationLogger.LogResponseAsync(response, cancellationToken);
                    }

                    _providerManager.RecordSuccess(provider);
                    return;
                }

                if (statusCode == 503 || statusCode == 429)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_providersConfig.RetryDelaySeconds), cancellationToken);
                }

                _providerManager.RecordError(provider, "Upstream returned non-success status", statusCode: statusCode, responseBody: responseBody);
                await _conversationLogger.LogFailureAsync(
                    CreateFailure(provider, conversationId, requestId, path, statusCode, responseBody), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.ProxyError(provider.BaseUrl, ex.Message, ex);
                _providerManager.RecordError(provider, ex.Message, ex);
                await _conversationLogger.LogFailureAsync(
                    CreateFailure(provider, conversationId, requestId, path, null, ex.Message), cancellationToken);
            }
        }

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }

    private static ConversationFailure CreateFailure(
        ProviderConfig provider,
        string conversationId,
        string requestId,
        string path,
        int? statusCode,
        string? body) =>
        new(conversationId, requestId, DateTimeOffset.UtcNow, provider.Name, provider.Model, path, statusCode, body);

    /// <summary>
    /// Captures a JSON copy of the request body for the audit log without consuming it: the stream is always
    /// rewound so forwarding is unaffected.
    /// </summary>
    /// <param name="request">Incoming request, already buffered.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A JSON element describing the body: the parsed body, a multipart summary or the raw text.</returns>
    private async Task<JsonElement> CaptureAuditBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (IsMultipartRequest(request))
        {
            return ToElement(BuildMultipartAuditBody(request));
        }

        var (text, truncated) = await ReadAuditTextAsync(request, cancellationToken);

        JsonNode audit;
        if (IsJsonRequest(request) && TryParseJson(text, out var parsed) && parsed is not null)
        {
            audit = parsed;
        }
        else
        {
            audit = new JsonObject { ["raw"] = text };
        }

        if (truncated)
        {
            _logger.ProxyAuditBodyTruncated(MaxAuditBodyBytes);

            if (audit is JsonObject auditObject)
            {
                auditObject["auditTruncated"] = true;
            }
        }

        return ToElement(audit);
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
            _logger.ProxyAuditBodyUnavailable(ex.Message);
        }

        return audit;
    }

    /// <summary>Model the client asked for, read from the audit body (multipart requests carry it in the fields).</summary>
    private static string? RequestedModel(JsonElement body)
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
    private static ConversationResponse AuditBufferedResponse(
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

    private async Task<(bool Success, int? StatusCode, string? ResponseBody, ConversationResponse? Response)> TryForwardAsync(
        HttpContext context,
        ProviderConfig provider,
        string path,
        string conversationId,
        string requestId,
        CancellationToken cancellationToken)
    {
        var upstreamUrl = BuildUpstreamUrl(provider.BaseUrl, path, context.Request.QueryString);

        using var upstreamRequest = new HttpRequestMessage
        {
            Method = new HttpMethod(context.Request.Method),
            RequestUri = new Uri(upstreamUrl)
        };

        // Forward request headers, replacing Authorization with provider key
        foreach (var header in context.Request.Headers)
        {
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                _hopByHopHeaders.Contains(header.Key))
            {
                continue;
            }

            if (!upstreamRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
            {
                upstreamRequest.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        upstreamRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {provider.ApiKey}");

        // Forward request body, substituting the configured model
        if (HttpMethodHasBody(context.Request.Method))
        {
            upstreamRequest.Content = IsMultipartRequest(context.Request)
                ? BuildMultipartContent(context.Request, provider.Model)
                : await BuildJsonContentAsync(context.Request, provider.Model, provider.SendMaxCompletionTokens, cancellationToken);
        }

        var httpClient = _httpClientFactory.CreateClient();
        var stopwatch = Stopwatch.StartNew();
        using var upstreamResponse = await httpClient.SendAsync(
            upstreamRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!upstreamResponse.IsSuccessStatusCode)
        {
            // Try to read and log response body for error details
            string? responseBody = null;
            try
            {
                responseBody = await upstreamResponse.Content.ReadAsStringAsync();
                // Limit response body length to prevent excessive logging
                if (responseBody?.Length > 1000)
                {
                    responseBody = responseBody.Substring(0, 1000) + "...[truncated]";
                }
            }
            catch
            {
                // If we can't read the response body, just log the status code
            }
            
            if (responseBody != null)
            {
                _logger.ProxyUpstreamErrorWithBody(provider.BaseUrl, (int)upstreamResponse.StatusCode, responseBody);
            }
            else
            {
                _logger.ProxyUpstreamError(provider.BaseUrl, (int)upstreamResponse.StatusCode);
            }
            
            return (false, (int)upstreamResponse.StatusCode, responseBody, null);
        }

        // Write response status and headers before streaming the body
        context.Response.StatusCode = (int)upstreamResponse.StatusCode;
        CopyResponseHeaders(context.Response, upstreamResponse);

        var isEventStream = upstreamResponse.Content.Headers.ContentType?.MediaType
            ?.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase) == true;

        if (isEventStream)
        {
            // Disable response buffering for SSE so chunks reach the client immediately
            context.Response.Headers.CacheControl = "no-cache";
            var responseBodyFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
            responseBodyFeature?.DisableBuffering();

            // Read line-by-line so we can extract usage from the final data chunk
            using var upstreamStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(upstreamStream, Encoding.UTF8);
            var assembler = new SseAssistantAssembler(_loggerFactory.CreateLogger<SseAssistantAssembler>());
            string? lastDataPayload = null;
            var packetCount = 0;
            var lastProgressLog = DateTime.UtcNow;
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                await context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), cancellationToken);
                if (line.StartsWith("data: ", StringComparison.Ordinal) && line.Length > 6 && line[6..] != "[DONE]")
                {
                    lastDataPayload = line[6..];
                    assembler.Append(lastDataPayload);
                    packetCount++;
                    _logger.ProxySsePacket(provider.BaseUrl, lastDataPayload.Length);

                    var now = DateTime.UtcNow;
                    if ((now - lastProgressLog).TotalSeconds >= 10)
                    {
                        _logger.ProxySseProgress(provider.BaseUrl, packetCount);
                        lastProgressLog = now;
                    }
                }
            }
            stopwatch.Stop();
            await context.Response.Body.FlushAsync(cancellationToken);

            if (lastDataPayload != null)
                await TryLogUsageAsync(Encoding.UTF8.GetBytes(lastDataPayload), provider, stopwatch.ElapsedMilliseconds, cancellationToken);

            return (true, null, null, new ConversationResponse(
                conversationId,
                requestId,
                DateTimeOffset.UtcNow,
                provider.Name,
                provider.Model,
                path,
                assembler.AssistantMessage,
                assembler.Usage,
                Streamed: true));
        }
        else
        {
            // Buffer the response so we can parse usage metrics before sending
            var bytes = await upstreamResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            stopwatch.Stop();
            await context.Response.Body.WriteAsync(bytes, cancellationToken);
            await TryLogUsageAsync(bytes, provider, stopwatch.ElapsedMilliseconds, cancellationToken);

            return (true, null, null, AuditBufferedResponse(
                bytes, provider, path, conversationId, requestId));
        }
    }

    private async Task TryLogUsageAsync(byte[] responseBytes, ProviderConfig provider, long elapsedMs, CancellationToken cancellationToken)
    {
        _logger.ProxyParsingUsage(provider.BaseUrl);
        try
        {
            var usage = ParseUsageFromResponse(responseBytes);
            if (usage is null)
            {
                _logger.ProxyNoUsageData(provider.BaseUrl);
                return;
            }

            var (promptTokens, completionTokens, totalTokens, cachedTokens) = usage.Value;
            if (totalTokens == 0)
            {
                _logger.ProxyNoUsageData(provider.BaseUrl);
                return;
            }

            var elapsedSeconds = elapsedMs / 1000.0;
            var promptTokensPerSec = elapsedSeconds > 0 ? promptTokens / elapsedSeconds : 0;
            var completionTokensPerSec = elapsedSeconds > 0 ? completionTokens / elapsedSeconds : 0;

            _logger.ProxyUsage(provider.BaseUrl, elapsedMs, promptTokensPerSec, completionTokensPerSec);

            await _tokenCountLogger.LogTokenCountAsync(new TokenCountLog(
                Date: DateTime.UtcNow,
                Provider: provider.Name,
                Model: provider.Model,
                PromptTokens: promptTokens,
                CompletionTokens: completionTokens,
                TotalTokens: totalTokens,
                CachedTokens: cachedTokens,
                RequestCount: 1,
                ElapsedMs: elapsedMs
            ), cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.ProxyNoUsageData(provider.BaseUrl, ex);
        }
    }

    internal (int promptTokens, int completionTokens, int totalTokens, int cachedTokens)? ParseUsageFromResponse(byte[] responseBytes)
    {
        using var doc = JsonDocument.Parse(responseBytes);
        if (!doc.RootElement.TryGetProperty("usage", out var usage))
        {
            return null;
        }

        var promptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
        var completionTokens = usage.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0;
        var totalTokens = usage.TryGetProperty("total_tokens", out var tt) ? tt.GetInt32() : 0;
        var cachedTokens = 0;

        if (usage.TryGetProperty("prompt_tokens_details", out var promptTokensDetails))
        {
            cachedTokens = promptTokensDetails.TryGetProperty("cached_tokens", out var ct2) ? ct2.GetInt32() : 0;
        }

        return (promptTokens, completionTokens, totalTokens, cachedTokens);
    }

    internal static string BuildUpstreamUrl(string baseUrl, string path, QueryString queryString)
    {
        var url = $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
        if (queryString.HasValue)
        {
            url += queryString.Value;
        }
        return url;
    }

    private static void CopyResponseHeaders(HttpResponse response, HttpResponseMessage upstreamResponse)
    {
        foreach (var header in upstreamResponse.Headers)
        {
            if (_hopByHopHeaders.Contains(header.Key))
            {
                continue;
            }
            response.Headers.TryAdd(header.Key, header.Value.ToArray());
        }

        foreach (var header in upstreamResponse.Content.Headers)
        {
            // Skip Content-Length — ASP.NET sets it correctly after the body is written
            if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            response.Headers.TryAdd(header.Key, header.Value.ToArray());
        }
    }

    internal static bool HttpMethodHasBody(string method) =>
        method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
        method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
        method.Equals("PATCH", StringComparison.OrdinalIgnoreCase);

    internal static bool IsMultipartRequest(HttpRequest request) =>
        request.ContentType?.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsJsonRequest(HttpRequest request) =>
        request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true;

    internal static async Task<HttpContent> BuildJsonContentAsync(
        HttpRequest request, string model, bool sendMaxCompletionTokens, CancellationToken cancellationToken)
    {
        if (!IsJsonRequest(request))
        {
            var raw = new StreamContent(request.Body);
            if (request.ContentType is { } ct)
                raw.Headers.ContentType = MediaTypeHeaderValue.Parse(ct);
            return raw;
        }

        using var doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("model", out _))
        {
            // No model field — return body as-is (reset position for the stream)
            request.Body.Position = 0;
            var raw = new StreamContent(request.Body);
            raw.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            return raw;
        }

        var isStreaming = doc.RootElement.TryGetProperty("stream", out var streamProp)
            && streamProp.ValueKind == JsonValueKind.True;
        var hasStreamOptions = doc.RootElement.TryGetProperty("stream_options", out _);

        var ms = new MemoryStream();
        await using var writer = new Utf8JsonWriter(ms);
        writer.WriteStartObject();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name.Equals("model", StringComparison.OrdinalIgnoreCase))
                writer.WriteString("model", model);
            else if (prop.Name.Equals("max_tokens", StringComparison.OrdinalIgnoreCase) && sendMaxCompletionTokens)
            {
                writer.WritePropertyName("max_completion_tokens");
                prop.Value.WriteTo(writer);
            }
            else
                prop.WriteTo(writer);
        }
        if (isStreaming && !hasStreamOptions)
        {
            writer.WritePropertyName("stream_options");
            writer.WriteStartObject();
            writer.WriteBoolean("include_usage", true);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
        ms.Position = 0;

        var content = new StreamContent(ms);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        return content;
    }

    internal static MultipartFormDataContent BuildMultipartContent(HttpRequest request, string model)
    {
        var multipart = new MultipartFormDataContent();

        foreach (var field in request.Form)
        {
            var value = field.Key.Equals("model", StringComparison.OrdinalIgnoreCase)
                ? model
                : field.Value.ToString();
            multipart.Add(new StringContent(value), field.Key);
        }

        foreach (var file in request.Form.Files)
        {
            var fileContent = new StreamContent(file.OpenReadStream());
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
                string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType);
            multipart.Add(fileContent, file.Name, file.FileName);
        }

        return multipart;
    }

    private ProviderConfig? GetProviderForRequest(HttpContext context)
    {
        var bestProvider = _providerManager.GetBestProvider();
        if (bestProvider == null)
            return null;

        if (!IsJsonRequest(context.Request))
            return bestProvider;

        var model = ExtractModelFromRequest(context.Request);
        if (string.IsNullOrEmpty(model))
            return bestProvider;

        if (model.Equals("llm-proxy:auto", StringComparison.OrdinalIgnoreCase))
            return bestProvider;

        if (model.Contains(':'))
        {
            var parts = model.Split(':', 2);
            var providerName = parts[0];
            return _providerManager.GetAllProviders().FirstOrDefault(p => 
                p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
        }

        return bestProvider;
    }

    private string? ExtractModelFromRequest(HttpRequest request)
    {
        try
        {
            request.Body.Position = 0;
            using var doc = JsonDocument.Parse(request.Body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("model", out var modelProp))
                {
                    return modelProp.ValueKind == JsonValueKind.String ? modelProp.GetString() : null;
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }
}
