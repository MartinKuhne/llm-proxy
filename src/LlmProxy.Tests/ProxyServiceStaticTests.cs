namespace LlmProxy.Tests;

using LlmProxy.Application.Services;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using System.Text;
using System.Text.Json;

public class ProxyServiceStaticTests
{
    [Theory]
    [InlineData("http://api.example.com", "v1/chat/completions", "", "http://api.example.com/v1/chat/completions")]
    [InlineData("http://api.example.com/", "v1/chat/completions", "", "http://api.example.com/v1/chat/completions")]
    [InlineData("http://api.example.com", "/v1/chat/completions", "", "http://api.example.com/v1/chat/completions")]
    [InlineData("http://api.example.com", "v1/chat/completions", "?stream=true", "http://api.example.com/v1/chat/completions?stream=true")]
    [InlineData("http://api.example.com/", "/v1/chat/completions", "?stream=true", "http://api.example.com/v1/chat/completions?stream=true")]
    public void BuildUpstreamUrl_ReturnsCorrectUrl(string baseUrl, string path, string queryString, string expected)
    {
        var result = ProxyService.BuildUpstreamUrl(baseUrl, path, new QueryString(queryString));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("POST", true)]
    [InlineData("PUT", true)]
    [InlineData("PATCH", true)]
    [InlineData("GET", false)]
    [InlineData("DELETE", false)]
    [InlineData("HEAD", false)]
    public void HttpMethodHasBody_ReturnsCorrectValue(string method, bool expected)
    {
        var result = ProxyService.HttpMethodHasBody(method);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("multipart/form-data", true)]
    [InlineData("multipart/related", true)]
    [InlineData("application/json", false)]
    [InlineData("text/plain", false)]
    public void IsMultipartRequest_ReturnsCorrectValue(string contentType, bool expected)
    {
        var request = Substitute.For<HttpRequest>();
        request.ContentType.Returns(contentType);

        var result = ProxyService.IsMultipartRequest(request);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("application/json", true)]
    [InlineData("application/json; charset=utf-8", true)]
    [InlineData("text/plain", false)]
    public void IsJsonRequest_ReturnsCorrectValue(string contentType, bool expected)
    {
        var request = Substitute.For<HttpRequest>();
        request.ContentType.Returns(contentType);

        var result = ProxyService.IsJsonRequest(request);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task BuildJsonContentAsync_WithNonJsonRequest_ReturnsRawContent()
    {
        var request = Substitute.For<HttpRequest>();
        request.ContentType.Returns("text/plain");
        request.Body.Returns(new MemoryStream());

        var result = await ProxyService.BuildJsonContentAsync(request, "test-model", true, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("text/plain", result.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task BuildJsonContentAsync_WithJsonRequestNoModel_ReturnsRawContent()
    {
        var json = """{"messages":[{"role":"user","content":"test"}]}""";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var request = Substitute.For<HttpRequest>();
        request.ContentType.Returns("application/json");
        request.Body.Returns(stream);
        request.Body.Position = 0L;

        var result = await ProxyService.BuildJsonContentAsync(request, "test-model", true, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("application/json", result.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task BuildJsonContentAsync_WithMaxTokensSupportedFalse_RenamesMaxTokensToMaxCompletionTokens()
    {
        var json = """{"model":"test","max_tokens":100,"messages":[{"role":"user","content":"test"}]}""";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        stream.Position = 0L;
        var request = Substitute.For<HttpRequest>();
        request.ContentType.Returns("application/json");
        request.Body.Returns(stream);

        var result = await ProxyService.BuildJsonContentAsync(request, "replaced-model", true, CancellationToken.None);

        Assert.NotNull(result);
        var content = await result.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        
        Assert.False(doc.RootElement.TryGetProperty("max_tokens", out _));
        Assert.True(doc.RootElement.TryGetProperty("max_completion_tokens", out var maxCompletionTokens));
        Assert.Equal(100, maxCompletionTokens.GetInt32());
    }

    [Fact]
    public async Task BuildJsonContentAsync_WithMaxTokensSupportedTrue_KeepsMaxTokens()
    {
        var json = """{"model":"test","max_tokens":100,"messages":[{"role":"user","content":"test"}]}""";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        stream.Position = 0L;
        var request = Substitute.For<HttpRequest>();
        request.ContentType.Returns("application/json");
        request.Body.Returns(stream);

        var result = await ProxyService.BuildJsonContentAsync(request, "replaced-model", false, CancellationToken.None);

        Assert.NotNull(result);
        var content = await result.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        
        Assert.True(doc.RootElement.TryGetProperty("max_tokens", out var maxTokens));
        Assert.Equal(100, maxTokens.GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("max_completion_tokens", out _));
    }

    [Fact]
    public void BuildMultipartContent_ReturnsMultipartContent()
    {
        var form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["model"] = "test-model",
            ["prompt"] = "test prompt"
        });

        var request = Substitute.For<HttpRequest>();
        request.Form.Returns(form);

        var result = ProxyService.BuildMultipartContent(request, "replaced-model");

        Assert.NotNull(result);
        Assert.IsType<MultipartFormDataContent>(result);
    }
}
