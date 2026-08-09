using System.Text.Json;
using LlmProxy.Domain;

namespace LlmProxy.Application.Configuration;

public class ProvidersConfig
{
    public IEnumerable<ProviderConfig> Providers { get; set; } = new List<ProviderConfig>();
    public string? CurrentProvider { get; set; }
    public int RetryDelaySeconds { get; set; } = 60;
    public string OpenRouterApiKey { get; set; } = "";
    
    public void LoadFromJson(string json)
    {
        var config = JsonSerializer.Deserialize<ProvidersConfig>(json);
        if (config != null)
        {
            this.Providers = config.Providers;
            this.CurrentProvider = config.CurrentProvider;
        }
    }
    
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
    }
    
    public void Validate()
    {
        foreach (var provider in Providers)
        {
            if (string.IsNullOrEmpty(provider.Name))
                throw new InvalidOperationException($"Provider at index {Array.IndexOf(Providers.ToArray(), provider)} has empty Name");
                
            if (string.IsNullOrEmpty(provider.BaseUrl))
                throw new InvalidOperationException($"Provider '{provider.Name}' has empty BaseUrl");
                
            if (string.IsNullOrEmpty(provider.Model))
                throw new InvalidOperationException($"Provider '{provider.Name}' has empty Model");
                
            if (provider.Complexity < 0 || provider.Complexity > 10)
                throw new InvalidOperationException($"Provider '{provider.Name}' has invalid Complexity {provider.Complexity}. Must be between 0 and 10");
                
            if (provider.Cost < 0 || provider.Cost > 10)
                throw new InvalidOperationException($"Provider '{provider.Name}' has invalid Cost {provider.Cost}. Must be between 0 and 10");
                
            if (provider.Capabilities != null)
            {
                foreach (var kvp in provider.Capabilities)
                {
                    var capability = kvp.Value;
                    if (capability.Cost < 0 || capability.Cost > 10)
                        throw new InvalidOperationException($"Provider '{provider.Name}' has invalid Capability '{capability.Name}' Cost {capability.Cost}. Must be between 0 and 10");
                        
                    if (capability.Complexity < 0 || capability.Complexity > 10)
                        throw new InvalidOperationException($"Provider '{provider.Name}' has invalid Capability '{capability.Name}' Complexity {capability.Complexity}. Must be between 0 and 10");
                        
                    if (capability.Name == "*")
                    {
                        throw new InvalidOperationException($"Provider '{provider.Name}' has a '*' capability entry. Use 'Capabilities': '*' at the provider level instead.");
                    }
                }
            }
        }
    }
}