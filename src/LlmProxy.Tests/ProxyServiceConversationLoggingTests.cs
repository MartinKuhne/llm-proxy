namespace LlmProxy.Tests;

using LlmProxy.Application.Configuration;
using LlmProxy.Application.Services;
using LlmProxy.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Text;
using System.Text.Json;

/// <summary>
/// Verifies the proxy hands prompt, response, tool call and failure information to the conversation audit log.
/// </summary>
public class ProxyServiceConversationLoggingTests
{
    private const string Prompt = "What is the weather in Berlin?";

    private readonly IProviderManager _providerManager;
    private readonly ITokenCountLogger _tokenCountLogger;
    private readonly ILogger<ProxyService> _logger;
    private readonly IConversationLogger _conversationLogger;
    private readonly List<string> _sequence = new();
    private readonly List<ConversationRequest> _requests = new();
    private readonly List<ConversationResponse> _responses = new();
    private readonly List<ConversationFailure> _failures = new();
    private readonly ProviderConfig _primary;
    private readonly ProviderConfig _backup;

    public ProxyServiceConversationLoggingTests()
    {
        _providerManager = Substitute.For<IProviderManager>();
        _tokenCountLogger = Substitute.For<ITokenCountLogger>();
        _logger = Substitute.For<ILogger<ProxyService>>();
        _conversationLogger = Substitute.For<IConversationLogger>();

        _primary = new ProviderConfig("Primary", "http://primary.com", "key1", 1, "model-primary");
        _backup = new ProviderConfig("Backup", "http://backup.com", "key2", 2, "model-backup");

        _providerManager.GetBestProvider().Returns(_primary);
        _providerManager.GetAllProviders().Returns(new[] { _primary, _backup });

        _conversationLogger
            .LogRequestAsync(Arg.Any<ConversationRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _sequence.Add("request");
                _requests.Add(call.Arg<ConversationRequest>());
                return Task.CompletedTask;
            });

        _conversationLogger
            .LogResponseAsync(Arg.Any<ConversationResponse>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _sequence.Add("response");
                _responses.Add(call.Arg<ConversationResponse>());
                return Task.CompletedTask;
            });

        _conversationLogger
            .LogFailureAsync(Arg.Any<ConversationFailure>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _sequence.Add("failure");
                _failures.Add(call.Arg<ConversationFailure>());
                return Task.CompletedTask;
            });
    }

    [Fact]
    public async Task ProxyAsync_LogsTheRequestOnceBeforeCallingUpstream()
    {
        var service = CreateService(async _ =>
        {
            _sequence.Add("upstream");
            return JsonResponse(CompletionJson("sunny"));
        });

        await service.ProxyAsync(Context(), "v1/chat/completions");

        Assert.Equal(new[] { "request", "upstream", "response" }, _sequence);
        await _conversationLogger.Received(1).LogRequestAsync(Arg.Any<ConversationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProxyAsync_LogsTheUserPromptOfTheRequest()
    {
        var service = CreateService(_ => Task.FromResult(JsonResponse(CompletionJson("sunny"))));

        await service.ProxyAsync(Context(), "v1/chat/completions");

        Assert.Contains(Prompt, _requests.Single().Body.ToString(), StringComparison.Ordinal);
        Assert.Equal("requested-model", _requests.Single().Model);
    }

    [Fact]
    public async Task ProxyAsync_UsesTheConversationHeaderForEveryEventOfOneRequest()
    {
        var service = CreateService(_ => Task.FromResult(JsonResponse(CompletionJson("sunny"))));

        await service.ProxyAsync(Context(conversationId: "chat-77"), "v1/chat/completions");

        Assert.Equal("chat-77", _requests.Single().ConversationId);
        Assert.Equal("chat-77", _responses.Single().ConversationId);
        Assert.Equal(_requests.Single().RequestId, _responses.Single().RequestId);
    }

    [Fact]
    public async Task ProxyAsync_NonStreamingSuccess_LogsAssistantContentUsageAndStreamedFalse()
    {
        var service = CreateService(_ => Task.FromResult(JsonResponse(
            """
            {"id":"1","choices":[{"index":0,"message":{"role":"assistant","content":"Sunny in Berlin","finish_reason":"stop"}}],"usage":{"prompt_tokens":9,"completion_tokens":5,"total_tokens":14}}
            """)));

        await service.ProxyAsync(Context(), "v1/chat/completions");

        var response = _responses.Single();
        Assert.False(response.Streamed);
        Assert.Equal("Sunny in Berlin", response.AssistantMessage!.Value.GetProperty("content").GetString());
        Assert.Equal("stop", response.AssistantMessage.Value.GetProperty("finish_reason").GetString());
        Assert.Equal(14, response.Usage!.Value.GetProperty("total_tokens").GetInt32());
        Assert.Equal("Primary", response.Provider);
        Assert.Equal("model-primary", response.Model);
    }

    [Fact]
    public async Task ProxyAsync_NonChatResponse_LogsTheRawBody()
    {
        var service = CreateService(_ => Task.FromResult(JsonResponse(
            """{"object":"list","data":[{"object":"embedding","index":0,"embedding":[0.1,0.2]}]}""")));

        await service.ProxyAsync(Context(), "v1/embeddings");

        var response = _responses.Single();
        Assert.Null(response.AssistantMessage);
        Assert.Equal("list", response.RawResponse!.Value.GetProperty("object").GetString());
    }

    [Fact]
    public async Task ProxyAsync_StreamingSuccess_LogsAssembledContentToolCallsAndStreamedTrue()
    {
        var service = CreateService(_ => Task.FromResult(StreamResponse(
            """
            data: {"choices":[{"index":0,"delta":{"role":"assistant","content":"Sunny"}}]}

            data: {"choices":[{"index":0,"delta":{"reasoning":"checking the forecast"}}]}

            data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"get_weather","arguments":"{\"city\":"}}]}}]}

            data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"Berlin\"}"}}]},"finish_reason":"tool_calls"}]}

            data: {"choices":[],"usage":{"prompt_tokens":11,"completion_tokens":4,"total_tokens":15}}

            data: [DONE]

            """)));

        await service.ProxyAsync(Context(), "v1/chat/completions");

        var response = _responses.Single();
        Assert.True(response.Streamed);
        Assert.Equal("Sunny", response.AssistantMessage!.Value.GetProperty("content").GetString());
        Assert.Equal("checking the forecast", response.AssistantMessage.Value.GetProperty("reasoning").GetString());
        Assert.Equal("tool_calls", response.AssistantMessage.Value.GetProperty("finish_reason").GetString());

        var call = response.AssistantMessage.Value.GetProperty("tool_calls")[0];
        Assert.Equal("call_1", call.GetProperty("id").GetString());
        Assert.Equal("get_weather", call.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("""{"city":"Berlin"}""", call.GetProperty("function").GetProperty("arguments").GetString());

        Assert.Equal(15, response.Usage!.Value.GetProperty("total_tokens").GetInt32());
    }

    [Fact]
    public async Task ProxyAsync_NonSuccessUpstream_LogsFailureWithThatStatusCode()
    {
        OnlyPrimaryProvider();
        var service = CreateService(_ => Task.FromResult(
            new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"error":"slow down"}"""),
            }));

        await service.ProxyAsync(Context(), "v1/chat/completions");

        var failure = _failures.Single();
        Assert.Equal(429, failure.StatusCode);
        Assert.Contains("slow down", failure.Body, StringComparison.Ordinal);
        Assert.Equal("Primary", failure.Provider);
        Assert.Equal(_requests.Single().ConversationId, failure.ConversationId);
        Assert.Empty(_responses);
    }

    [Fact]
    public async Task ProxyAsync_ThrowingUpstream_LogsFailureWithoutStatusCode()
    {
        OnlyPrimaryProvider();
        var service = CreateService(_ => throw new HttpRequestException("Connection refused"));

        await service.ProxyAsync(Context(), "v1/chat/completions");

        var failure = _failures.Single();
        Assert.Null(failure.StatusCode);
        Assert.Equal("Connection refused", failure.Body);
        Assert.Empty(_responses);
    }

    [Fact]
    public async Task ProxyAsync_FirstProviderFails_LogsOneRequestOneFailureOneResponse()
    {
        var service = CreateService(request => request.RequestUri!.Host == "primary.com"
            ? Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("""{"error":"boom"}"""),
            })
            : Task.FromResult(JsonResponse(CompletionJson("clear skies"))));

        await service.ProxyAsync(Context(), "v1/chat/completions");

        Assert.Single(_requests);
        Assert.Single(_failures);
        Assert.Single(_responses);
        Assert.Equal("Backup", _responses.Single().Provider);
        Assert.Equal("clear skies", _responses.Single().AssistantMessage!.Value.GetProperty("content").GetString());
    }

    [Fact]
    public async Task ProxyAsync_ForwardsTheCompleteRequestBodyAfterCapturingIt()
    {
        string? forwarded = null;
        var service = CreateService(async request =>
        {
            forwarded = await request.Content!.ReadAsStringAsync();
            return JsonResponse(CompletionJson("sunny"));
        });

        await service.ProxyAsync(Context(), "v1/chat/completions");

        Assert.NotNull(forwarded);
        using var document = JsonDocument.Parse(forwarded);
        var root = document.RootElement;
        Assert.Equal("model-primary", root.GetProperty("model").GetString());
        Assert.Equal(2, root.GetProperty("messages").GetArrayLength());
        Assert.Equal(Prompt, root.GetProperty("messages")[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ProxyAsync_MultipartRequest_LogsASummaryWithoutBinaryContent()
    {
        var service = CreateService(_ => Task.FromResult(JsonResponse("""{"text":"hello"}""")));

        await service.ProxyAsync(MultipartContext(), "v1/audio/transcriptions");

        var body = _requests.Single().Body;
        Assert.True(body.GetProperty("multipart").GetBoolean());
        Assert.Equal("whisper-1", body.GetProperty("fields").GetProperty("model").GetString());
        Assert.Equal("audio.wav", body.GetProperty("files")[0].GetProperty("fileName").GetString());
        Assert.Equal("audio/wav", body.GetProperty("files")[0].GetProperty("contentType").GetString());
        Assert.Equal("whisper-1", _requests.Single().Model);
    }

    [Fact]
    public async Task ProxyAsync_WithoutProviders_StillLogsTheRequestOnce()
    {
        _providerManager.GetBestProvider().Returns((ProviderConfig?)null);
        _providerManager.GetAllProviders().Returns(Array.Empty<ProviderConfig>());
        var service = CreateService(_ => Task.FromResult(JsonResponse(CompletionJson("never"))));
        var context = Context();

        await service.ProxyAsync(context, "v1/chat/completions");

        Assert.Single(_requests);
        Assert.Empty(_responses);
        Assert.Empty(_failures);
        Assert.Equal(503, context.Response.StatusCode);
    }

    [Fact]
    public async Task ProxyAsync_OversizedBody_CapsTheAuditCopyAndForwardsEverything()
    {
        var oversized = $$"""{"model":"requested-model","messages":[{"role":"user","content":"{{new string('x', 2_100_000)}}"}]}""";
        string? forwarded = null;
        var service = CreateService(async request =>
        {
            forwarded = await request.Content!.ReadAsStringAsync();
            return JsonResponse(CompletionJson("sunny"));
        });

        await service.ProxyAsync(Context(body: oversized), "v1/chat/completions");

        var audit = _requests.Single().Body;
        Assert.True(audit.GetProperty("auditTruncated").GetBoolean());
        Assert.Equal(2_000_000, audit.GetProperty("raw").GetString()!.Length);

        Assert.NotNull(forwarded);
        using var forwardedDoc = JsonDocument.Parse(forwarded);
        Assert.Equal("model-primary", forwardedDoc.RootElement.GetProperty("model").GetString());
        Assert.Equal(
            2_100_000,
            forwardedDoc.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!.Length);
    }

    private void OnlyPrimaryProvider() => _providerManager.GetAllProviders().Returns(new[] { _primary });

    private ProxyService CreateService(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        var httpClient = new HttpClient(new TestHandler(handler));
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient().Returns(httpClient);

        return new ProxyService(
            _logger,
            _providerManager,
            httpClientFactory,
            _tokenCountLogger,
            new ProvidersConfig { RetryDelaySeconds = 0 },
            _conversationLogger,
            new ConversationIdResolver(NullLogger<ConversationIdResolver>.Instance),
            new AuditBodyCapture(NullLogger<AuditBodyCapture>.Instance),
            NullLoggerFactory.Instance);
    }

    private static DefaultHttpContext Context(string? conversationId = null, string? body = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body ??
            $$"""{"model":"requested-model","messages":[{"role":"system","content":"You are terse"},{"role":"user","content":"{{Prompt}}"}]}"""));
        context.Response.Body = new MemoryStream();

        if (conversationId is not null)
        {
            context.Request.Headers[ConversationIdResolver.HeaderName] = conversationId;
        }

        return context;
    }

    private static DefaultHttpContext MultipartContext()
    {
        const string Boundary = "----audit-test";
        var body =
            $"--{Boundary}\r\nContent-Disposition: form-data; name=\"model\"\r\n\r\nwhisper-1\r\n" +
            $"--{Boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"audio.wav\"\r\nContent-Type: audio/wav\r\n\r\nfake-audio-bytes\r\n" +
            $"--{Boundary}--\r\n";

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = $"multipart/form-data; boundary={Boundary}";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string CompletionJson(string content) =>
        JsonSerializer.Serialize(new
        {
            id = "chatcmpl-1",
            choices = new[] { new { index = 0, message = new { role = "assistant", content }, finish_reason = "stop" } },
            usage = new { prompt_tokens = 3, completion_tokens = 2, total_tokens = 5 },
        });

    private static HttpResponseMessage JsonResponse(string body) =>
        new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage StreamResponse(string events) =>
        new(System.Net.HttpStatusCode.OK) { Content = new StringContent(events, Encoding.UTF8, "text/event-stream") };

    private sealed class TestHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public TestHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => _handler(request);
    }
}
