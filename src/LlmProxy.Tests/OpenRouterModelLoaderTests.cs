using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LlmProxy.Application.Configuration;
using LlmProxy.Application.Services;
using LlmProxy.Domain;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace LlmProxy.Tests;

public class OpenRouterModelLoaderTests
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenRouterModelLoader> _logger;

    public OpenRouterModelLoaderTests()
    {
        _httpClientFactory = Substitute.For<IHttpClientFactory>();
        _logger = Substitute.For<ILogger<OpenRouterModelLoader>>();
    }

    private OpenRouterModelLoader CreateLoader(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        _httpClientFactory.CreateClient().Returns(httpClient);
        return new OpenRouterModelLoader(_httpClientFactory, _logger);
    }

    [Fact]
    public async Task LoadFreeModelsAsync_AppendsFreeModels_WithBasePriority500()
    {
        // Arrange
        var jsonResponse = """
        {
          "data": [
            {
              "id": "model/free-model",
              "name": "Free Model",
              "description": "A free model",
              "pricing": {
                "prompt": "0",
                "completion": "0"
              }
            },
            {
              "id": "model/paid-model",
              "name": "Paid Model",
              "description": "A paid model",
              "pricing": {
                "prompt": "0.0001",
                "completion": "0.0002"
              }
            }
          ]
        }
        """;

        var handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var loader = CreateLoader(handler);
        var config = new ProvidersConfig
        {
            Providers = new List<ProviderConfig>
            {
                new ProviderConfig("Existing", "http://existing.com", "key", 100, "model1")
            }
        };

        // Act
        await loader.LoadFreeModelsAsync(config);

        // Assert
        var providers = config.Providers.ToList();
        Assert.Equal(2, providers.Count);

        var existing = providers.First(p => p.Name == "Existing");
        Assert.Equal(100, existing.Priority);
        Assert.False(existing.AutoDiscovered);

        var freeModel = providers.First(p => p.Name == "OpenRouter");
        Assert.Equal("model/free-model", freeModel.Model);
        Assert.True(freeModel.AutoDiscovered);
        Assert.Equal(500, freeModel.Priority);
        Assert.Equal("https://openrouter.ai/api/v1", freeModel.BaseUrl);
        Assert.True(freeModel.HasAllCapabilities);
        Assert.Equal(0, freeModel.Cost);
        Assert.Equal(0, freeModel.Complexity);

        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Added OpenRouter free model: model/free-model")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task LoadFreeModelsAsync_WhenNameOrIdContainsModelSize_AddsValueToPriority()
    {
        // Arrange
        var jsonResponse = """
        {
          "data": [
            {
              "id": "model/free-model-550b",
              "name": "Free Model 550b",
              "description": "A free 550B model",
              "pricing": {
                "prompt": "0",
                "completion": "0"
              }
            },
            {
              "id": "model/free-model-330b",
              "name": "Free Model 330b",
              "description": "A free 330B model",
              "pricing": {
                "prompt": "0",
                "completion": "0"
              }
            },
            {
              "id": "model/free-model-10B",
              "name": "Free Model 10B",
              "description": "A free 10B model",
              "pricing": {
                "prompt": "0",
                "completion": "0"
              }
            },
            {
              "id": "model/free-model-normal",
              "name": "NVIDIA: Nemotron 3 Ultra (free)",
              "description": "Another free model without size suffix",
              "pricing": {
                "prompt": "0",
                "completion": "0"
              }
            }
          ]
        }
        """;

        var handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var loader = CreateLoader(handler);
        var config = new ProvidersConfig();

        // Act
        await loader.LoadFreeModelsAsync(config);

        // Assert
        var providers = config.Providers.ToList();
        Assert.Equal(4, providers.Count);
        
        var model550 = providers.First(p => p.Model == "model/free-model-550b");
        Assert.Equal(1050, model550.Priority);

        var model330 = providers.First(p => p.Model == "model/free-model-330b");
        Assert.Equal(830, model330.Priority);

        var model10 = providers.First(p => p.Model == "model/free-model-10B");
        Assert.Equal(510, model10.Priority);

        var modelNormal = providers.First(p => p.Model == "model/free-model-normal");
        Assert.Equal(500, modelNormal.Priority);
    }

    [Fact]
    public async Task LoadFreeModelsAsync_HandlesApiErrorGracefully()
    {
        // Arrange
        var handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var loader = CreateLoader(handler);
        var config = new ProvidersConfig
        {
            Providers = new List<ProviderConfig>
            {
                new ProviderConfig("Existing", "http://existing.com", "key", 100, "model1")
            }
        };

        // Act
        var exception = await Record.ExceptionAsync(() => loader.LoadFreeModelsAsync(config));

        // Assert
        Assert.Null(exception); // Should not throw
        Assert.Single(config.Providers); // Should not have changed existing providers
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
