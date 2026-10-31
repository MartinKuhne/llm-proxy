namespace LlmProxy.Tests;

using System.Net;
using LlmProxy.Api.Middleware;
using LlmProxy.Application.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Verifies the operator surface only answers the machine the proxy runs on, and leaves the proxied API alone.
/// </summary>
public class AdminAccessMiddlewareTests
{
    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/")]
    [InlineData("/admin/conversations")]
    [InlineData("/admin/conversation/abc123")]
    [InlineData("/admin/api/conversations")]
    [InlineData("/api/providers")]
    [InlineData("/api/providers/active")]
    [InlineData("/admin.html")]
    [InlineData("/conversations.html")]
    [InlineData("/conversation.html")]
    public async Task RemoteClient_OnAnOperatorPath_IsRefused(string path)
    {
        var (context, reached) = await Run(path, IPAddress.Parse("192.168.1.50"));

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task RemoteClient_IsRefusedWhateverTheMethod()
    {
        var (context, reached) = await Run("/admin/api/conversations", IPAddress.Parse("10.0.0.9"), "POST");

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.2.3.4")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public async Task LoopbackClient_OnAnOperatorPath_IsLetThrough(string address)
    {
        var (context, reached) = await Run("/admin/api/conversations", IPAddress.Parse(address));

        Assert.True(reached);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/v1/chat/completions")]
    [InlineData("/v1/embeddings")]
    [InlineData("/v1/models")]
    [InlineData("/")]
    [InlineData("/health")]
    public async Task RemoteClient_OnTheProxiedApi_IsLetThrough(string path)
    {
        var (context, reached) = await Run(path, IPAddress.Parse("203.0.113.7"));

        Assert.True(reached);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/administrators")]
    [InlineData("/api/providers-status-elsewhere")]
    public async Task RemoteClient_OnAPathThatMerelyLooksLikeAnOperatorPath_IsLetThrough(string path)
    {
        var (context, reached) = await Run(path, IPAddress.Parse("203.0.113.7"));

        Assert.True(reached);
    }

    [Fact]
    public async Task RemoteClient_WithTheRestrictionTurnedOff_IsLetThrough()
    {
        var (_, reached) = await Run("/admin/api/conversations", IPAddress.Parse("203.0.113.7"), requireLocalhost: false);

        Assert.True(reached);
    }

    [Fact]
    public async Task ClientWithNoPeerAddress_IsRefused()
    {
        var (context, reached) = await Run("/admin", address: null);

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task RemoteClient_IsRefusedWithAJsonErrorBody()
    {
        var (context, _) = await Run("/admin/api/conversations", IPAddress.Parse("203.0.113.7"));

        context.Response.Body.Position = 0;
        var body = new StreamReader(context.Response.Body).ReadToEnd();
        Assert.Equal("application/json", context.Response.ContentType);
        Assert.Contains("restricted to the machine the proxy runs on", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OperatorPathMatching_IsCaseInsensitive()
    {
        var (_, reached) = await Run("/ADMIN/api/conversations", IPAddress.Parse("203.0.113.7"));

        Assert.False(reached);
    }

    private static async Task<(DefaultHttpContext Context, bool Reached)> Run(
        string path,
        IPAddress? address,
        string method = "GET",
        bool requireLocalhost = true)
    {
        var reached = false;
        var middleware = new AdminAccessMiddleware(
            _ =>
            {
                reached = true;
                return Task.CompletedTask;
            },
            new AdminAccessOptions { RequireLocalhost = requireLocalhost },
            NullLogger<AdminAccessMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = address;
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        return (context, reached);
    }
}
