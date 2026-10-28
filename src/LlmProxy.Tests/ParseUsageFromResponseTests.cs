namespace LlmProxy.Tests;

using LlmProxy.Application.Configuration;
using LlmProxy.Application.Services;
using NSubstitute;
using System.Text;
using System.Text.Json;

public class ParseUsageFromResponseTests
{
    private readonly ProxyService _proxyService;

    public ParseUsageFromResponseTests()
    {
        _proxyService = new ProxyService(
            logger: new Microsoft.Extensions.Logging.Abstractions.NullLogger<ProxyService>(),
            providerManager: Substitute.For<IProviderManager>(),
            httpClientFactory: Substitute.For<IHttpClientFactory>(),
            tokenCountLogger: Substitute.For<ITokenCountLogger>(),
            providersConfig: new ProvidersConfig(),
            conversationLogger: Substitute.For<IConversationLogger>(),
            conversationIdResolver: new ConversationIdResolver(),
            auditBodyCapture: new AuditBodyCapture(new Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditBodyCapture>()),
            loggerFactory: Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
    }

    [Fact]
    public void ParseUsageFromResponse_WithValidUsage_ReturnsTokens()
    {
        var json = """{"usage":{"prompt_tokens":10,"completion_tokens":20,"total_tokens":30}}""";
        var bytes = Encoding.UTF8.GetBytes(json);

        var result = _proxyService.ParseUsageFromResponse(bytes);

        Assert.NotNull(result);
        Assert.Equal(10, result.Value.promptTokens);
        Assert.Equal(20, result.Value.completionTokens);
        Assert.Equal(30, result.Value.totalTokens);
    }

    [Fact]
    public void ParseUsageFromResponse_WithMissingUsage_ReturnsNull()
    {
        var json = """{"choices":[{"finish_reason":"stop"}]}""";
        var bytes = Encoding.UTF8.GetBytes(json);

        var result = _proxyService.ParseUsageFromResponse(bytes);

        Assert.Null(result);
    }

    [Fact]
    public void ParseUsageFromResponse_WithEmptyUsage_ReturnsZeros()
    {
        var json = """{"usage":{}}""";
        var bytes = Encoding.UTF8.GetBytes(json);

        var result = _proxyService.ParseUsageFromResponse(bytes);

        Assert.NotNull(result);
        Assert.Equal(0, result.Value.promptTokens);
        Assert.Equal(0, result.Value.completionTokens);
        Assert.Equal(0, result.Value.totalTokens);
    }

    [Fact]
    public void ParseUsageFromResponse_WithInvalidJson_ThrowsJsonException()
    {
        var json = """{invalid json}""";
        var bytes = Encoding.UTF8.GetBytes(json);

        Assert.ThrowsAny<JsonException>(() => _proxyService.ParseUsageFromResponse(bytes));
    }

    [Fact]
    public void ParseUsageFromResponse_WithNullBytes_ThrowsJsonException()
    {
        Assert.ThrowsAny<JsonException>(() => _proxyService.ParseUsageFromResponse(null!));
    }

    [Fact]
    public void ParseUsageFromResponse_WithEmptyBytes_ThrowsJsonException()
    {
        var bytes = Array.Empty<byte>();

        Assert.ThrowsAny<JsonException>(() => _proxyService.ParseUsageFromResponse(bytes));
    }

    [Fact]
    public void ParseUsageFromResponse_WithMissingTokenFields_ReturnsZeros()
    {
        var json = """{"usage":{"prompt_tokens":5}}""";
        var bytes = Encoding.UTF8.GetBytes(json);

        var result = _proxyService.ParseUsageFromResponse(bytes);

        Assert.NotNull(result);
        Assert.Equal(5, result.Value.promptTokens);
        Assert.Equal(0, result.Value.completionTokens);
        Assert.Equal(0, result.Value.totalTokens);
        Assert.Equal(0, result.Value.cachedTokens);
    }

    [Fact]
    public void ParseUsageFromResponse_WithCachedTokens_ReturnsCachedTokens()
    {
        var json = """{"usage":{"prompt_tokens":10,"completion_tokens":20,"total_tokens":30,"prompt_tokens_details":{"cached_tokens":5}}}""";
        var bytes = Encoding.UTF8.GetBytes(json);

        var result = _proxyService.ParseUsageFromResponse(bytes);

        Assert.NotNull(result);
        Assert.Equal(10, result.Value.promptTokens);
        Assert.Equal(20, result.Value.completionTokens);
        Assert.Equal(30, result.Value.totalTokens);
        Assert.Equal(5, result.Value.cachedTokens);
    }

    [Fact]
    public void ParseUsageFromResponse_WithPromptTokensDetailsButNoCachedTokens_ReturnsZeroCached()
    {
        var json = """{"usage":{"prompt_tokens":10,"completion_tokens":20,"total_tokens":30,"prompt_tokens_details":{}}}""";
        var bytes = Encoding.UTF8.GetBytes(json);

        var result = _proxyService.ParseUsageFromResponse(bytes);

        Assert.NotNull(result);
        Assert.Equal(10, result.Value.promptTokens);
        Assert.Equal(20, result.Value.completionTokens);
        Assert.Equal(30, result.Value.totalTokens);
        Assert.Equal(0, result.Value.cachedTokens);
    }
}
