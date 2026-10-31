namespace LlmProxy.Domain;

public record CapabilityConfig(
    string Name,
    int Cost,
    int Complexity,
    bool Supported,
    int? MaxTokens = null,
    int? MaxContexts = null,
    Dictionary<string, object>? Limits = null,
    Dictionary<string, object>? Features = null);

public record ProviderConfig(
    string Name,
    string BaseUrl,
    string ApiKey,
    int Priority,
    string Model,
    bool SendMaxCompletionTokens = false,
    int Cost = 0,
    int Complexity = 0,
    string? Description = null,
    Dictionary<string, CapabilityConfig>? Capabilities = null,
    bool HasAllCapabilities = true,
    bool AllowAutomatic = true,
    bool AutoDiscovered = false)
{
    /// <summary>
    /// Unique key derived from base URL and model, used for provider lookup and deduplication.
    /// </summary>
    public string ProviderKey => $"{BaseUrl}|{Model}";

    /// <summary>
    /// Human-readable key combining provider name and model, used for display and forced-provider selection.
    /// </summary>
    public string FullModelKey => $"{Name}:{Model}";
}