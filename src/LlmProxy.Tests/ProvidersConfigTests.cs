namespace LlmProxy.Tests;

using LlmProxy.Application.Configuration;
using LlmProxy.Domain;

public class ProvidersConfigTests
{
    [Fact]
    public void ProvidersConfig_HasEmptyProvidersByDefault()
    {
        var config = new ProvidersConfig();

        Assert.Empty(config.Providers);
    }

    [Fact]
    public void ProvidersConfig_CanSetProviders()
    {
        var config = new ProvidersConfig
        {
            Providers = new[]
            {
                new ProviderConfig("P1", "http://p1.com", "key1", 1, "model1", Cost: 5, Complexity: 5),
                new ProviderConfig("P2", "http://p2.com", "key2", 2, "model2", Cost: 2, Complexity: 8)
            }
        };

        Assert.Equal(2, config.Providers.Count());
    }

    [Fact]
    public void ProvidersConfig_CanSetCurrentProvider()
    {
        var config = new ProvidersConfig { CurrentProvider = "http://test.com|model" };

        Assert.Equal("http://test.com|model", config.CurrentProvider);
    }

    [Fact]
    public void ProviderConfig_HasMaxTokensSupportedFalseByDefault()
    {
        var provider = new ProviderConfig("P1", "http://p1.com", "key1", 1, "model1");

        Assert.False(provider.SendMaxCompletionTokens);
    }

    [Fact]
    public void ProviderConfig_CanSetMaxTokensSupportedToFalse()
    {
        var provider = new ProviderConfig("P1", "http://p1.com", "key1", 1, "model1", SendMaxCompletionTokens: false);

        Assert.False(provider.SendMaxCompletionTokens);
    }

    [Fact]
    public void ProviderConfig_HasDefaultsForCostAndComplexity()
    {
        var provider = new ProviderConfig("P1", "http://p1.com", "key1", 1, "model1");

        Assert.Equal(0, provider.Cost);
        Assert.Equal(0, provider.Complexity);
    }

    [Fact]
    public void ProviderConfig_CanSetCostAndComplexity()
    {
        var provider = new ProviderConfig("P1", "http://p1.com", "key1", 1, "model1", Cost: 5, Complexity: 10);

        Assert.Equal(5, provider.Cost);
        Assert.Equal(10, provider.Complexity);
    }

    [Fact]
    public void ProviderConfig_HasAllCapabilitiesDefaultsToTrue()
    {
        var provider = new ProviderConfig("P1", "http://p1.com", "key1", 1, "model1");

        Assert.True(provider.HasAllCapabilities);
    }

    [Fact]
    public void ProviderConfig_CanSetHasAllCapabilitiesToFalse()
    {
        var provider = new ProviderConfig(
            "P1", "http://p1.com", "key1", 1, "model1", 
            HasAllCapabilities: false);

        Assert.False(provider.HasAllCapabilities);
    }

    [Fact]
    public void ProviderConfig_CanSetCapabilities()
    {
        var capabilities = new Dictionary<string, CapabilityConfig>
        {
            ["chat"] = new CapabilityConfig("chat", 5, 7, true, 4096, 128000),
            ["embeddings"] = new CapabilityConfig("embeddings", 2, 3, true)
        };
        
        var provider = new ProviderConfig(
            "P1", "http://p1.com", "key1", 1, "model1", 
            Capabilities: capabilities);

        Assert.NotNull(provider.Capabilities);
        Assert.Equal(2, provider.Capabilities.Count);
        Assert.True(provider.Capabilities["chat"].Supported);
        Assert.Equal(5, provider.Capabilities["chat"].Cost);
        Assert.True(provider.Capabilities["embeddings"].Supported);
    }

    [Fact]
    public void CapabilityConfig_CanSetAllProperties()
    {
        var limits = new Dictionary<string, object> { ["max_requests_per_minute"] = 60 };
        var features = new Dictionary<string, object> { ["streaming"] = true };
        
        var capability = new CapabilityConfig(
            "chat", 8, 10, true, 8192, 200000, limits, features);

        Assert.Equal("chat", capability.Name);
        Assert.Equal(8, capability.Cost);
        Assert.Equal(10, capability.Complexity);
        Assert.True(capability.Supported);
        Assert.Equal(8192, capability.MaxTokens);
        Assert.Equal(200000, capability.MaxContexts);
        Assert.NotNull(capability.Limits);
        Assert.NotNull(capability.Features);
    }

    [Fact]
    public void ProviderKey_ReturnsBaseUrlPipeModel()
    {
        var provider = new ProviderConfig("P1", "http://p1.com", "key1", 1, "model1");

        Assert.Equal("http://p1.com|model1", provider.ProviderKey);
    }

    [Fact]
    public void ProviderKey_IsCaseInsensitiveCompatible()
    {
        var p1 = new ProviderConfig("P1", "HTTP://P1.COM", "key1", 1, "MODEL1");
        var p2 = new ProviderConfig("P1", "http://p1.com", "key1", 1, "model1");

        Assert.Equal(p1.ProviderKey.ToLowerInvariant(), p2.ProviderKey.ToLowerInvariant());
    }

    [Fact]
    public void FullModelKey_ReturnsNameColonModel()
    {
        var provider = new ProviderConfig("OpenAI", "http://api.openai.com", "key1", 1, "gpt-4");

        Assert.Equal("OpenAI:gpt-4", provider.FullModelKey);
    }

    [Fact]
    public void FullModelKey_IsUsedForForcedProviderSelection()
    {
        var provider = new ProviderConfig("MyProvider", "http://example.com", "key1", 1, "model1");

        Assert.Equal("MyProvider:model1", provider.FullModelKey);
    }
}
