namespace LlmProxy.Tests;

using System.Text;
using System.Text.Json;
using LlmProxy.Application.Services;
using LlmProxy.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Direct coverage of the audit body capture that was extracted out of <see cref="ProxyService"/>.
/// </summary>
public class AuditBodyCaptureTests
{
    private const string Boundary = "----audit-capture";

    private readonly AuditBodyCapture _capture = new(NullLogger<AuditBodyCapture>.Instance);

    [Fact]
    public async Task CaptureRequestAsync_JsonBody_IsStoredAsParsedJson()
    {
        var context = Request("application/json", """{"model":"gpt-4","messages":[{"role":"user","content":"hi"}]}""");

        var body = await _capture.CaptureRequestAsync(context.Request, CancellationToken.None);

        Assert.Equal("gpt-4", body.GetProperty("model").GetString());
        Assert.Equal("hi", body.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.False(body.TryGetProperty("raw", out _));
        Assert.False(body.TryGetProperty("auditTruncated", out _));
    }

    [Theory]
    [InlineData("text/plain", "not json at all")]
    [InlineData("application/json", "{ this is not json")]
    [InlineData("application/octet-stream", "\u0000\u0001binary-ish")]
    public async Task CaptureRequestAsync_BodyThatIsNotJson_IsStoredAsRawText(string contentType, string body)
    {
        var captured = await _capture.CaptureRequestAsync(Request(contentType, body).Request, CancellationToken.None);

        Assert.Equal(body, captured.GetProperty("raw").GetString());
    }

    [Fact]
    public async Task CaptureRequestAsync_RewindsTheBodySoForwardingStillSeesEverything()
    {
        const string json = """{"model":"gpt-4","messages":[]}""";
        var context = Request("application/json", json);

        await _capture.CaptureRequestAsync(context.Request, CancellationToken.None);

        Assert.Equal(0, context.Request.Body.Position);
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        Assert.Equal(json, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task CaptureRequestAsync_BodyOverTheCap_IsStoredTruncatedAndFlagged()
    {
        var oversized = new string('x', AuditBodyCapture.MaxAuditBodyBytes + 100_000);
        var context = Request("text/plain", oversized);

        var captured = await _capture.CaptureRequestAsync(context.Request, CancellationToken.None);

        Assert.True(captured.GetProperty("auditTruncated").GetBoolean());
        Assert.Equal(AuditBodyCapture.MaxAuditBodyBytes, captured.GetProperty("raw").GetString()!.Length);
        Assert.Equal(0, context.Request.Body.Position);
    }

    [Fact]
    public async Task CaptureRequestAsync_MultipartRequest_IsSummarisedWithoutBinaryContent()
    {
        var captured = await _capture.CaptureRequestAsync(MultipartRequest().Request, CancellationToken.None);

        Assert.True(captured.GetProperty("multipart").GetBoolean());
        Assert.Equal($"multipart/form-data; boundary={Boundary}", captured.GetProperty("contentType").GetString());
        Assert.Equal("whisper-1", captured.GetProperty("fields").GetProperty("model").GetString());
        Assert.Equal("translate", captured.GetProperty("fields").GetProperty("prompt").GetString());

        var file = captured.GetProperty("files")[0];
        Assert.Equal("file", file.GetProperty("name").GetString());
        Assert.Equal("audio.wav", file.GetProperty("fileName").GetString());
        Assert.Equal("audio/wav", file.GetProperty("contentType").GetString());
        Assert.Equal(16, file.GetProperty("size").GetInt64());
    }

    [Fact]
    public async Task CaptureRequestAsync_FormTheServerCannotRead_KeepsTheContentTypeAlone()
    {
        // A multipart request with no boundary: reading the form throws, the summary must not.
        var context = Request("multipart/form-data", "unreadable");

        var captured = await _capture.CaptureRequestAsync(context.Request, CancellationToken.None);

        Assert.True(captured.GetProperty("multipart").GetBoolean());
        Assert.Equal("multipart/form-data", captured.GetProperty("contentType").GetString());
        Assert.False(captured.TryGetProperty("fields", out _));
        Assert.False(captured.TryGetProperty("files", out _));
    }

    [Fact]
    public void RequestedModel_TopLevelModel_IsReturned()
    {
        Assert.Equal("gpt-4", _capture.RequestedModel(Json("""{"model":"gpt-4"}""")));
    }

    [Fact]
    public void RequestedModel_MultipartFieldModel_IsReturned()
    {
        var body = Json($$"""{"multipart":true,"fields":{"model":"whisper-1"},"files":[]}""");

        Assert.Equal("whisper-1", _capture.RequestedModel(body));
    }

    [Theory]
    [InlineData("""{"messages":[]}""")]
    [InlineData("""{"model":42}""")]
    [InlineData("""{"fields":{"model":7}}""")]
    [InlineData("""[]""")]
    [InlineData("\"just a string\"")]
    public void RequestedModel_WithoutAUsableModel_ReturnsNull(string json)
    {
        Assert.Null(_capture.RequestedModel(Json(json)));
    }

    [Fact]
    public void BuildBufferedResponse_ChatCompletion_ExtractsAssistantAndUsage()
    {
        var response = Buffered("""
            {"choices":[{"index":0,"message":{"role":"assistant","content":"Sunny","finish_reason":"stop"}}],"usage":{"prompt_tokens":9,"completion_tokens":5,"total_tokens":14}}
            """);

        Assert.False(response.Streamed);
        Assert.Equal("conv-1", response.ConversationId);
        Assert.Equal("req-1", response.RequestId);
        Assert.Equal("Primary", response.Provider);
        Assert.Equal("model-primary", response.Model);
        Assert.Equal("/v1/chat/completions", response.Path);
        Assert.Equal("Sunny", response.AssistantMessage!.Value.GetProperty("content").GetString());
        Assert.Equal("stop", response.AssistantMessage.Value.GetProperty("finish_reason").GetString());
        Assert.Equal(14, response.Usage!.Value.GetProperty("total_tokens").GetInt32());
        Assert.Null(response.RawResponse);
    }

    [Fact]
    public void BuildBufferedResponse_EndpointWithoutAssistantMessage_KeepsTheWholeBodyAsRaw()
    {
        var response = Buffered("""{"object":"list","data":[{"object":"embedding","index":0,"embedding":[0.1,0.2]}]}""");

        Assert.Null(response.AssistantMessage);
        Assert.Equal("list", response.RawResponse!.Value.GetProperty("object").GetString());
    }

    [Fact]
    public void BuildBufferedResponse_UnparseableUpstreamBody_LeavesThePayloadEmptyWithoutThrowing()
    {
        var response = Buffered("<html>gateway gave up</html>");

        Assert.Null(response.AssistantMessage);
        Assert.Null(response.Usage);
        Assert.Null(response.RawResponse);
    }

    private ConversationResponse Buffered(string body) =>
        _capture.BuildBufferedResponse(
            Encoding.UTF8.GetBytes(body),
            new ProviderConfig("Primary", "http://primary.com", "key1", 1, "model-primary"),
            "/v1/chat/completions",
            "conv-1",
            "req-1");

    private static DefaultHttpContext Request(string contentType, string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context;
    }

    private static DefaultHttpContext MultipartRequest()
    {
        var body =
            $"--{Boundary}\r\nContent-Disposition: form-data; name=\"model\"\r\n\r\nwhisper-1\r\n" +
            $"--{Boundary}\r\nContent-Disposition: form-data; name=\"prompt\"\r\n\r\ntranslate\r\n" +
            $"--{Boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"audio.wav\"\r\nContent-Type: audio/wav\r\n\r\nfake-audio-bytes\r\n" +
            $"--{Boundary}--\r\n";

        return Request($"multipart/form-data; boundary={Boundary}", body);
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
