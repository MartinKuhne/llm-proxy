namespace LlmProxy.Tests;

using LlmProxy.Application.Configuration;
using System.Text;
using System.Text.Json;
using LlmProxy.Application.Services;
using LlmProxy.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

public class ProxyServiceFailoverTests
{
    private readonly IProviderManager _providerManager;
    private readonly ITokenCountLogger _tokenCountLogger;
    private readonly ILogger<ProxyService> _logger;
    private readonly ProviderConfig _provider1;
    private readonly ProviderConfig _provider2;

    public ProxyServiceFailoverTests()
    {
        _providerManager = Substitute.For<IProviderManager>();
        _tokenCountLogger = Substitute.For<ITokenCountLogger>();
        _logger = Substitute.For<ILogger<ProxyService>>();

        _provider1 = new ProviderConfig("Provider1", "http://provider1.com", "key1", 1, "model1");
        _provider2 = new ProviderConfig("Provider2", "http://provider2.com", "key2", 2, "model2");

        _providerManager.GetBestProvider().Returns(_provider2);
        _providerManager.GetAllProviders().Returns(new[] { _provider2, _provider1 });
    }

    private ProxyService CreateService(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient().Returns(httpClient);
        return new ProxyService(_logger, _providerManager, httpClientFactory, _tokenCountLogger, new ProvidersConfig());
    }

    private static DefaultHttpContext CreateContext(string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task ProxyAsync_WhenFirstProviderThrows_FallsBackToSecondProvider()
    {
        var callCount = 0;
        var handler = new TestHandler(req =>
        {
            callCount++;
            if (req.RequestUri!.Host == "provider2.com")
                throw new HttpRequestException("Connection refused");

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { id = "1", choices = Array.Empty<object>() }),
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var service = CreateService(handler);
        var context = CreateContext("""{"model":"gpt-4","messages":[{"role":"user","content":"hi"}]}""");

        await service.ProxyAsync(context, "v1/chat/completions");

        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal(2, callCount);

        _providerManager.Received().RecordError(
            _provider2,
            Arg.Any<string>(),
            Arg.Any<Exception>());
        _providerManager.Received().RecordSuccess(_provider1);
    }

    [Fact]
    public async Task ProxyAsync_WhenFirstProviderReturns500_FallsBackToSecondProvider()
    {
        var callCount = 0;
        var handler = new TestHandler(req =>
        {
            callCount++;
            if (req.RequestUri!.Host == "provider2.com")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("""{"error":"internal error"}""")
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { id = "1", choices = Array.Empty<object>() }),
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var service = CreateService(handler);
        var context = CreateContext("""{"model":"gpt-4","messages":[{"role":"user","content":"hi"}]}""");

        await service.ProxyAsync(context, "v1/chat/completions");

        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal(2, callCount);

        _providerManager.Received().RecordError(
            _provider2,
            Arg.Is<string>(msg => msg.Contains("non-success status")),
            Arg.Any<Exception>(),
            Arg.Any<int?>(),
            Arg.Any<string?>());
        _providerManager.Received().RecordSuccess(_provider1);
    }

    [Fact]
    public async Task ProxyAsync_WhenAllProvidersFail_Returns502()
    {
        var handler = new TestHandler(_ => throw new HttpRequestException("Connection refused"));

        var service = CreateService(handler);
        var context = CreateContext("""{"model":"gpt-4","messages":[{"role":"user","content":"hi"}]}""");

        await service.ProxyAsync(context, "v1/chat/completions");

        Assert.Equal(502, context.Response.StatusCode);

        _providerManager.Received().RecordError(
            _provider2,
            Arg.Any<string>(),
            Arg.Any<Exception>(),
            Arg.Any<int?>(),
            Arg.Any<string?>());
        _providerManager.Received().RecordError(
            _provider2,
            Arg.Any<string>(),
            Arg.Any<Exception>(),
            Arg.Any<int?>(),
            Arg.Any<string?>());
    }

    private sealed class TestHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public TestHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
