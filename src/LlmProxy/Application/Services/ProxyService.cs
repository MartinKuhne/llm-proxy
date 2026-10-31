namespace LlmProxy.Application.Services;

using LlmProxy.Application.Configuration;
using LlmProxy.Application.Logging;
using LlmProxy.Domain;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public class ProxyService : IProxyService
{
    // Headers that must not be forwarded between hops
    private static readonly HashSet<string> _hopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Transfer-Encoding", "TE",
        "Trailers", "Upgrade", "Proxy-Authorization", "Proxy-Authenticate"
    };

    private readonly ILogger<ProxyService> _logger;
    private readonly IProviderManager _providerManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITokenCountLogger _tokenCountLogger;
    private readonly ProvidersConfig _providersConfig;

    public ProxyService(
        ILogger<ProxyService> logger,
        IProviderManager providerManager,
        IHttpClientFactory httpClientFactory,
        ITokenCountLogger tokenCountLogger,
        ProvidersConfig providersConfig)
    {
        _logger = logger;
        _providerManager = providerManager;
        _httpClientFactory = httpClientFactory;
        _tokenCountLogger = tokenCountLogger;
        _providersConfig = providersConfig;
    }

    /// <summary>
    /// Proxies requests to upstream providers with automatic failover.
    /// </summary>
    public async Task ProxyAsync(HttpContext context, string path, CancellationToken cancellationToken = default)
    {
        // Buffer the request body so it can be re-read on retry
        context.Request.EnableBuffering();

        var bestProvider = GetProviderForRequest(context);
        if (bestProvider == null)
        {
            _logger.ProxyNoProviders();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        var providers = new[] { bestProvider }
            .Concat(_providerManager.GetAllProviders()
                .Where(p => p != bestProvider && p.AllowAutomatic && !p.AutoDiscovered));

        foreach (var provider in providers)
        {
            if (context.Response.HasStarted)
                return;

            context.Request.Body.Position = 0;

            try
            {
                var (forwarded, statusCode, responseBody) = await TryForwardAsync(context, provider, path, cancellationToken);
                if (forwarded)
                {
                    _providerManager.RecordSuccess(provider);
                    return;
                }

                if (statusCode == 503 || statusCode == 429)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_providersConfig.RetryDelaySeconds), cancellationToken);
                }

                _providerManager.RecordError(provider, "Upstream returned non-success status", statusCode: statusCode, responseBody: responseBody);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.ProxyError(provider.BaseUrl, ex.Message, ex);
                _providerManager.RecordError(provider, ex.Message, ex);
            }
        }

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }

    private async Task<(bool Success, int? StatusCode, string? ResponseBody)> TryForwardAsync(
        HttpContext context,
        ProviderConfig provider,
        string path,
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
            
            return (false, (int)upstreamResponse.StatusCode, responseBody);
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
        }
        else
        {
            // Buffer the response so we can parse usage metrics before sending
            var bytes = await upstreamResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            stopwatch.Stop();
            await context.Response.Body.WriteAsync(bytes, cancellationToken);
            await TryLogUsageAsync(bytes, provider, stopwatch.ElapsedMilliseconds, cancellationToken);
        }

        return (true, null, null);
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
