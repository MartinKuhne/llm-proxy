using System.Collections.Generic;

namespace LlmProxy.Application.Services;

using LlmProxy.Domain;

 public interface IProviderManager
{
    ProviderState? GetProviderState(ProviderConfig provider);
    void RecordSuccess(ProviderConfig provider);
    void RecordError(ProviderConfig provider, string errorMessage, Exception? exception = null, int? statusCode = null, string? responseBody = null);
    void RecordError(ProviderConfig provider, ProviderError error);
    ProviderConfig? GetBestProvider();
    ProviderConfig? GetForcedProvider();
    void SetForcedProvider(string? providerKey);
    IEnumerable<ProviderConfig> GetAllProviders();
    ProviderConfig? GetProviderForCapability(string capability, int? maxCost = null, int? maxComplexity = null);
    IEnumerable<ProviderConfig> GetCapableProviders(string capability);
    bool ProviderSupportsCapability(ProviderConfig provider, string capability);
    CapabilityConfig GetCapabilityConfig(ProviderConfig provider, string capability);
}