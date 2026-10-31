namespace LlmProxy.Tests;

using LlmProxy.Application.Configuration;
using LlmProxy.Application.Services;
using LlmProxy.Domain;

public class ProviderManagerTests
{
    private readonly ProviderManager _manager;

    public ProviderManagerTests()
    {
        var config = new ProvidersConfig
        {
            Providers = new[]
            {
                new ProviderConfig("Provider1", "http://provider1.com", "key1", 1, "model1"),
                new ProviderConfig("Provider2", "http://provider2.com", "key2", 2, "model2")
            }
        };
        _manager = new ProviderManager(config);
    }

    [Fact]
    public void GetBestProvider_ReturnsProviderWithHighestPriority()
    {
        var result = _manager.GetBestProvider();

        Assert.NotNull(result);
        Assert.Equal(2, result!.Priority);
    }

    [Fact]
    public void GetBestProvider_WithForcedProvider_ReturnsForcedProvider()
    {
        _manager.SetForcedProvider("http://provider2.com|model2");

        var result = _manager.GetBestProvider();

        Assert.NotNull(result);
        Assert.Equal("http://provider2.com", result!.BaseUrl);
        Assert.Equal("model2", result.Model);
        Assert.Equal("key2", result.ApiKey);
        Assert.Equal("Provider2", result.Name);
    }

    [Fact]
    public void GetBestProvider_WithNullForcedProvider_ReturnsHighestPriority()
    {
        _manager.SetForcedProvider(null);

        var result = _manager.GetBestProvider();

        Assert.NotNull(result);
        Assert.Equal(2, result!.Priority);
    }

    [Fact]
    public void GetForcedProvider_ReturnsForcedProvider()
    {
        _manager.SetForcedProvider("http://provider2.com|model2");

        var result = _manager.GetForcedProvider();

        Assert.NotNull(result);
        Assert.Equal("http://provider2.com", result!.BaseUrl);
    }

    [Fact]
    public void RecordSuccess_ResetsProviderState()
    {
        var provider = _manager.GetAllProviders().First();
        _manager.RecordError(provider, "test error");

        _manager.RecordSuccess(provider);

        var state = _manager.GetProviderState(provider);
        Assert.NotNull(state);
        Assert.Equal(0, state!.ErrorCount);
        Assert.Null(state.LastError);
    }

    [Fact]
    public void RecordError_IncrementsErrorCount()
    {
        var provider = _manager.GetAllProviders().First();

        _manager.RecordError(provider, "error 1");
        _manager.RecordError(provider, "error 2");

        var state = _manager.GetProviderState(provider);
        Assert.NotNull(state);
        Assert.Equal(2, state!.ErrorCount);
    }

    [Fact]
    public void GetAllProviders_ReturnsAllProviders()
    {
        var result = _manager.GetAllProviders();

        Assert.Equal(2, result.Count());
    }

    [Fact]
    public void GetProviderState_WithUnknownProvider_ReturnsNull()
    {
        var unknownProvider = new ProviderConfig("Unknown", "http://unknown.com", "key", 999, "unknown");
        var result = _manager.GetProviderState(unknownProvider);

        Assert.Null(result);
    }

    [Fact]
    public void RecordError_OnForcedProvider_With3Errors_RevertsToAuto()
    {
        var provider2 = _manager.GetAllProviders().First(p => p.Name == "Provider2");
        _manager.SetForcedProvider("http://provider2.com|model2");

        _manager.RecordError(provider2, "error 1");
        _manager.RecordError(provider2, "error 2");
        _manager.RecordError(provider2, "error 3");

        Assert.Null(_manager.GetForcedProvider());
    }

    [Fact]
    public void RecordError_OnForcedProvider_With2Errors_StillForced()
    {
        var provider2 = _manager.GetAllProviders().First(p => p.Name == "Provider2");
        _manager.SetForcedProvider("http://provider2.com|model2");

        _manager.RecordError(provider2, "error 1");
        _manager.RecordError(provider2, "error 2");

        Assert.NotNull(_manager.GetForcedProvider());
    }

    [Fact]
    public void RecordError_OnNonForcedProvider_DoesNotAffectForcedProvider()
    {
        var provider1 = _manager.GetAllProviders().First(p => p.Name == "Provider1");
        var provider2 = _manager.GetAllProviders().First(p => p.Name == "Provider2");
        _manager.SetForcedProvider("http://provider2.com|model2");

        _manager.RecordError(provider1, "error 1");
        _manager.RecordError(provider1, "error 2");
        _manager.RecordError(provider1, "error 3");

        Assert.NotNull(_manager.GetForcedProvider());
    }

    [Fact]
    public void GetBestProvider_ExcludesAutoDiscoveredProviders()
    {
        var config = new ProvidersConfig
        {
            Providers = new[]
            {
                new ProviderConfig("Configured", "http://configured.com", "key", 1, "configured-model"),
                new ProviderConfig("Discovered", "http://discovered.com", "key", 1000, "discovered-model", AutoDiscovered: true)
            }
        };
        var manager = new ProviderManager(config);

        var result = manager.GetBestProvider();

        Assert.NotNull(result);
        Assert.Equal("Configured", result!.Name);
    }

    [Fact]
    public void GetBestProvider_WithForcedAutoDiscoveredProvider_ReturnsForcedProvider()
    {
        var config = new ProvidersConfig
        {
            Providers = new[]
            {
                new ProviderConfig("Configured", "http://configured.com", "key", 1, "configured-model"),
                new ProviderConfig("Discovered", "http://discovered.com", "key", 1000, "discovered-model", AutoDiscovered: true)
            }
        };
        var manager = new ProviderManager(config);

        manager.SetForcedProvider("http://discovered.com|discovered-model");

        var result = manager.GetBestProvider();

        Assert.NotNull(result);
        Assert.Equal("Discovered", result!.Name);
    }

    [Fact]
    public void GetBestProvider_WithOnlyAutoDiscoveredProviders_ReturnsNull()
    {
        var config = new ProvidersConfig
        {
            Providers = new[]
            {
                new ProviderConfig("Discovered", "http://discovered.com", "key", 1000, "discovered-model", AutoDiscovered: true)
            }
        };
        var manager = new ProviderManager(config);

        var result = manager.GetBestProvider();

        Assert.Null(result);
    }

    [Fact]
    public void GetProviderForCapability_ExcludesAutoDiscoveredProviders()
    {
        var config = new ProvidersConfig
        {
            Providers = new[]
            {
                new ProviderConfig("Configured", "http://configured.com", "key", 1, "configured-model", HasAllCapabilities: true),
                new ProviderConfig("Discovered", "http://discovered.com", "key", 1000, "discovered-model", HasAllCapabilities: true, AutoDiscovered: true)
            }
        };
        var manager = new ProviderManager(config);

        var provider = manager.GetProviderForCapability("chat");

        Assert.NotNull(provider);
        Assert.Equal("Configured", provider!.Name);
    }

    [Fact]
    public void GetBestProvider_WithRecentError_ExcludesProvider()
    {
        var provider2 = _manager.GetAllProviders().First(p => p.Name == "Provider2");
        _manager.RecordError(provider2, "error");

        var best = _manager.GetBestProvider();

        Assert.NotNull(best);
        Assert.Equal("Provider1", best!.Name);
    }

    [Fact]
    public void GetBestProvider_AfterErrorRecovery_IncludesProvider()
    {
        var provider2 = _manager.GetAllProviders().First(p => p.Name == "Provider2");
        _manager.RecordError(provider2, "error");

        // Simulate recovery by setting LastError to more than 1 hour ago
        var field = typeof(ProviderManager).GetField(
            "_providerStates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var states = (System.Collections.Concurrent.ConcurrentDictionary<string, ProviderState>)field!.GetValue(_manager)!;
        var key = $"http://provider2.com|model2";
        var oldState = states[key];
        states[key] = oldState with { LastError = DateTime.UtcNow.AddHours(-2) };

        var best = _manager.GetBestProvider();

        Assert.NotNull(best);
        Assert.Equal("Provider2", best!.Name);
    }

    [Fact]
    public void GetBestProvider_AfterRecordSuccess_IncludesProvider()
    {
        var provider2 = _manager.GetAllProviders().First(p => p.Name == "Provider2");
        _manager.RecordError(provider2, "error");

        // Verify excluded while error is recent
        Assert.Equal("Provider1", _manager.GetBestProvider()!.Name);

        _manager.RecordSuccess(provider2);

        // Verify included again after success
        Assert.Equal("Provider2", _manager.GetBestProvider()!.Name);
    }

    [Fact]
    public void ProviderSupportsCapability_WithAllCapabilities_ReturnsTrue()
    {
        var config = new ProvidersConfig
        {
            Providers = new[]
            {
                new ProviderConfig("Universal", "http://universal.com", "key1", 1, "model1", HasAllCapabilities: true)
            }
        };
        var manager = new ProviderManager(config);
        var provider = manager.GetAllProviders().First();

        Assert.True(manager.ProviderSupportsCapability(provider, "chat"));
        Assert.True(manager.ProviderSupportsCapability(provider, "embeddings"));
        Assert.True(manager.ProviderSupportsCapability(provider, "transcriptions"));
    }

    [Fact]
    public void ProviderSupportsCapability_WithSpecificCapabilities_ReturnsCorrectly()
    {
        var capabilities = new Dictionary<string, CapabilityConfig>
        {
            ["chat"] = new CapabilityConfig("chat", 5, 7, true),
            ["embeddings"] = new CapabilityConfig("embeddings", 2, 3, false)
        };
        var config = new ProvidersConfig
        {
            Providers = new[]
            {
                new ProviderConfig("Specialized", "http://specialized.com", "key1", 1, "model1", 
                    Capabilities: capabilities, HasAllCapabilities: false)
            }
        };
        var manager = new ProviderManager(config);
        var provider = manager.GetAllProviders().First();

        Assert.True(manager.ProviderSupportsCapability(provider, "chat"));
        Assert.False(manager.ProviderSupportsCapability(provider, "embeddings"));
        Assert.False(manager.ProviderSupportsCapability(provider, "transcriptions"));
    }

    [Fact]
    public void GetCapableProviders_ReturnsOnlyProvidersWithCapability()
    {
        var capabilities = new Dictionary<string, CapabilityConfig>
        {
            ["chat"] = new CapabilityConfig("chat", 5, 7, true)
        };
        var config = new ProvidersConfig
        {
            Providers = new[]
            {
                new ProviderConfig("ChatOnly", "http://chat.com", "key1", 2, "chat-model", Capabilities: capabilities),
                new ProviderConfig("Universal", "http://universal.com", "key2", 1, "universal-model", HasAllCapabilities: true)
            }
        };
        var manager = new ProviderManager(config);

        var chatProviders = manager.GetCapableProviders("chat").ToList();

        Assert.Equal(2, chatProviders.Count);
    }

    [Fact]
    public void GetProviderForCapability_ReturnsBestProvider()
    {
        var capabilities = new Dictionary<string, CapabilityConfig>
        {
            ["chat"] = new CapabilityConfig("chat", 5, 7, true)
        };
        var config = new ProvidersConfig
        {
            Providers = new[]
            {
                new ProviderConfig("ChatOnly", "http://chat.com", "key1", 2, "chat-model", Capabilities: capabilities),
                new ProviderConfig("Universal", "http://universal.com", "key2", 1, "universal-model", HasAllCapabilities: true)
            }
        };
        var manager = new ProviderManager(config);

        var provider = manager.GetProviderForCapability("chat");

        Assert.NotNull(provider);
        Assert.Equal("ChatOnly", provider!.Name);
    }

    [Fact]
    public void GetProviderForCapability_WithNoProviders_ReturnsNull()
    {
        var capabilities = new Dictionary<string, CapabilityConfig>
        {
            ["chat"] = new CapabilityConfig("chat", 5, 7, false)
        };
        var config = new ProvidersConfig
        {
            Providers = new[]
            {
                new ProviderConfig("NoChat", "http://nochat.com", "key1", 1, "no-chat-model", 
                    Capabilities: capabilities, HasAllCapabilities: false)
            }
        };
        var manager = new ProviderManager(config);

        var provider = manager.GetProviderForCapability("chat");

        Assert.Null(provider);
    }

    [Fact]
    public void GetCapabilityConfig_ReturnsCorrectConfig()
    {
        var capabilities = new Dictionary<string, CapabilityConfig>
        {
            ["chat"] = new CapabilityConfig("chat", 8, 10, true, 4096)
        };
        var config = new ProvidersConfig
        {
            Providers = new[]
            {
                new ProviderConfig("Specialized", "http://specialized.com", "key1", 1, "model1", 
                    Capabilities: capabilities, HasAllCapabilities: false)
            }
        };
        var manager = new ProviderManager(config);
        var provider = manager.GetAllProviders().First();

        var capConfig = manager.GetCapabilityConfig(provider, "chat");

        Assert.Equal("chat", capConfig.Name);
        Assert.Equal(8, capConfig.Cost);
        Assert.Equal(10, capConfig.Complexity);
        Assert.True(capConfig.Supported);
        Assert.Equal(4096, capConfig.MaxTokens);
    }

    [Fact]
    public void GetCapabilityConfig_ForAllCapabilities_ReturnsProviderDefaults()
    {
        var config = new ProvidersConfig
        {
            Providers = new[]
            {
                new ProviderConfig("Universal", "http://universal.com", "key1", 1, "model1", Cost: 7, Complexity: 5, HasAllCapabilities: true)
            }
        };
        var manager = new ProviderManager(config);
        var provider = manager.GetAllProviders().First();

        var capConfig = manager.GetCapabilityConfig(provider, "chat");

        Assert.Equal("chat", capConfig.Name);
        Assert.Equal(7, capConfig.Cost);
        Assert.Equal(5, capConfig.Complexity);
        Assert.True(capConfig.Supported);
    }

    [Fact]
    public void GetProviderForCapability_ExcludesProvidersWithErrors()
    {
        var capabilities = new Dictionary<string, CapabilityConfig>
        {
            ["chat"] = new CapabilityConfig("chat", 5, 7, true)
        };
        var config = new ProvidersConfig
        {
            Providers = new[]
            {
                new ProviderConfig("Failing", "http://failing.com", "key1", 3, "failing-model", Capabilities: capabilities),
                new ProviderConfig("Working", "http://working.com", "key2", 2, "working-model", Capabilities: capabilities)
            }
        };
        var manager = new ProviderManager(config);
        
        var failingProvider = manager.GetAllProviders().First(p => p.Name == "Failing");
        manager.RecordError(failingProvider, "error");

        var provider = manager.GetProviderForCapability("chat");

        Assert.NotNull(provider);
        Assert.Equal("Working", provider!.Name);
    }
}
