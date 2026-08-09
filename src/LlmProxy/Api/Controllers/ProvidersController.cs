namespace LlmProxy.Api.Controllers;

using LlmProxy.Api.Requests;
using LlmProxy.Api.Responses;
using LlmProxy.Application.Configuration;
using LlmProxy.Application.Services;
using LlmProxy.Domain;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/providers")]
public class ProvidersController : ControllerBase
{
    private readonly IProviderManager _providerManager;
    private readonly ProvidersConfig _providersConfig;

    public ProvidersController(
        IProviderManager providerManager,
        ProvidersConfig providersConfig)
    {
        _providerManager = providerManager;
        _providersConfig = providersConfig;
    }

    /// <summary>
    /// Get all configured providers.
    /// </summary>
    [HttpGet]
    public IActionResult GetProviders()
    {
        var providers = _providerManager.GetAllProviders().Select(p => new ProviderInfo
        {
            Name = p.Name,
            BaseUrl = p.BaseUrl,
            ApiKey = p.ApiKey,
            Priority = p.Priority,
            Model = p.Model,
            SendMaxCompletionTokens = p.SendMaxCompletionTokens,
            Cost = p.Cost,
            Complexity = p.Complexity,
            Description = p.Description,
            Capabilities = p.Capabilities?.Select(c => new CapabilityInfo
            {
                Name = c.Value.Name,
                Cost = c.Value.Cost,
                Complexity = c.Value.Complexity,
                Supported = c.Value.Supported,
                MaxTokens = c.Value.MaxTokens,
                MaxContexts = c.Value.MaxContexts,
                Limits = c.Value.Limits,
                Features = c.Value.Features
            }).ToList(),
            HasAllCapabilities = p.HasAllCapabilities,
            AllowAutomatic = p.AllowAutomatic
        }).ToList();

        return Ok(new ProvidersList { Providers = providers, RetryDelaySeconds = _providersConfig.RetryDelaySeconds });
    }

    /// <summary>
    /// Get a specific provider by key.
    /// </summary>
    [HttpGet("{key}")]
    public IActionResult GetProvider(string key)
    {
        var provider = _providerManager.GetAllProviders().FirstOrDefault(p =>
            p.ProviderKey == key || p.FullModelKey == key);

        if (provider == null)
        {
            return NotFound();
        }

        var providerInfo = new ProviderInfo
        {
            Name = provider.Name,
            BaseUrl = provider.BaseUrl,
            ApiKey = provider.ApiKey,
            Priority = provider.Priority,
            Model = provider.Model,
            SendMaxCompletionTokens = provider.SendMaxCompletionTokens,
            Cost = provider.Cost,
            Complexity = provider.Complexity,
            Description = provider.Description,
            Capabilities = provider.Capabilities?.Select(c => new CapabilityInfo
            {
                Name = c.Value.Name,
                Cost = c.Value.Cost,
                Complexity = c.Value.Complexity,
                Supported = c.Value.Supported,
                MaxTokens = c.Value.MaxTokens,
                MaxContexts = c.Value.MaxContexts,
                Limits = c.Value.Limits,
                Features = c.Value.Features
            }).ToList(),
            HasAllCapabilities = provider.HasAllCapabilities,
            AllowAutomatic = provider.AllowAutomatic
        };

        return Ok(providerInfo);
    }

    /// <summary>
    /// Add a new provider.
    /// </summary>
    [HttpPost]
    public IActionResult AddProvider([FromBody] ProviderInfo providerInfo)
    {
        if (string.IsNullOrWhiteSpace(providerInfo.Name))
        {
            return BadRequest("Provider name is required");
        }

        if (string.IsNullOrWhiteSpace(providerInfo.BaseUrl))
        {
            return BadRequest("Provider base URL is required");
        }

        if (string.IsNullOrWhiteSpace(providerInfo.Model))
        {
            return BadRequest("Provider model is required");
        }

        var existingProvider = _providerManager.GetAllProviders().FirstOrDefault(p =>
            p.Name.Equals(providerInfo.Name, StringComparison.OrdinalIgnoreCase));

        if (existingProvider != null)
        {
            return BadRequest($"Provider '{providerInfo.Name}' already exists");
        }

        var capabilities = MapCapabilities(providerInfo.Capabilities);

        var newProvider = new ProviderConfig(
            Name: providerInfo.Name ?? string.Empty,
            BaseUrl: providerInfo.BaseUrl ?? string.Empty,
            ApiKey: providerInfo.ApiKey ?? string.Empty,
            Priority: providerInfo.Priority,
            Model: providerInfo.Model ?? string.Empty,
            SendMaxCompletionTokens: providerInfo.SendMaxCompletionTokens ?? false,
            Cost: providerInfo.Cost,
            Complexity: providerInfo.Complexity,
            Description: providerInfo.Description,
            Capabilities: capabilities,
            HasAllCapabilities: providerInfo.HasAllCapabilities ?? true,
            AllowAutomatic: providerInfo.AllowAutomatic ?? true);

        var providersList = _providersConfig.Providers.ToList();
        providersList.Add(newProvider);
        _providersConfig.Providers = providersList;

        return CreatedAtAction(nameof(GetProvider), new { key = newProvider.ProviderKey }, newProvider);
    }

    /// <summary>
    /// Update an existing provider.
    /// </summary>
    [HttpPut("{key}")]
    public IActionResult UpdateProvider(string key, [FromBody] ProviderInfo providerInfo)
    {
        var existingProvider = _providerManager.GetAllProviders().FirstOrDefault(p =>
            p.ProviderKey == key || p.FullModelKey == key);

        if (existingProvider == null)
        {
            return NotFound();
        }

        var updatedProvider = new ProviderConfig(
            Name: providerInfo.Name ?? existingProvider.Name,
            BaseUrl: providerInfo.BaseUrl ?? existingProvider.BaseUrl,
            ApiKey: providerInfo.ApiKey ?? existingProvider.ApiKey,
            Priority: providerInfo.Priority >= 0 ? providerInfo.Priority : existingProvider.Priority,
            Model: providerInfo.Model ?? existingProvider.Model,
            SendMaxCompletionTokens: providerInfo.SendMaxCompletionTokens.HasValue 
                ? providerInfo.SendMaxCompletionTokens.Value 
                : existingProvider.SendMaxCompletionTokens,
            Cost: providerInfo.Cost >= 0 ? providerInfo.Cost : existingProvider.Cost,
            Complexity: providerInfo.Complexity >= 0 ? providerInfo.Complexity : existingProvider.Complexity,
            Description: providerInfo.Description ?? existingProvider.Description,
            Capabilities: MapCapabilities(providerInfo.Capabilities) ?? existingProvider.Capabilities,
            HasAllCapabilities: providerInfo.HasAllCapabilities ?? existingProvider.HasAllCapabilities,
            AllowAutomatic: providerInfo.AllowAutomatic ?? existingProvider.AllowAutomatic);

        var providersList = _providersConfig.Providers.ToList();
        var index = providersList.FindIndex(p => p.ProviderKey == key || p.FullModelKey == key);
        providersList[index] = updatedProvider;
        _providersConfig.Providers = providersList;

        return Ok(new ProviderInfo
        {
            Name = updatedProvider.Name,
            BaseUrl = updatedProvider.BaseUrl,
            ApiKey = updatedProvider.ApiKey,
            Priority = updatedProvider.Priority,
            Model = updatedProvider.Model,
            SendMaxCompletionTokens = updatedProvider.SendMaxCompletionTokens,
            Cost = updatedProvider.Cost,
            Complexity = updatedProvider.Complexity,
            Description = updatedProvider.Description,
            Capabilities = updatedProvider.Capabilities?.Select(c => new CapabilityInfo
            {
                Name = c.Value.Name,
                Cost = c.Value.Cost,
                Complexity = c.Value.Complexity,
                Supported = c.Value.Supported,
                MaxTokens = c.Value.MaxTokens,
                MaxContexts = c.Value.MaxContexts,
                Limits = c.Value.Limits,
                Features = c.Value.Features
            }).ToList(),
            HasAllCapabilities = updatedProvider.HasAllCapabilities,
            AllowAutomatic = updatedProvider.AllowAutomatic
        });
    }

    /// <summary>
    /// Delete a provider.
    /// </summary>
    [HttpDelete("{key}")]
    public IActionResult DeleteProvider(string key)
    {
        var provider = _providerManager.GetAllProviders().FirstOrDefault(p =>
            p.ProviderKey == key || p.FullModelKey == key);

        if (provider == null)
        {
            return NotFound();
        }

        var providersList = _providersConfig.Providers.Where(p =>
            p.ProviderKey != key && p.FullModelKey != key).ToList();

        if (providersList.Count == 0)
        {
            return BadRequest("Cannot delete all providers");
        }

        _providersConfig.Providers = providersList;

        return NoContent();
    }

    /// <summary>
    /// Get retry delay configuration.
    /// </summary>
    [HttpGet("retry-config")]
    public IActionResult GetRetryConfig()
    {
        return Ok(new RetryConfig { RetryDelaySeconds = _providersConfig.RetryDelaySeconds });
    }

    /// <summary>
    /// Update retry delay configuration.
    /// </summary>
    [HttpPatch("retry-config")]
    public IActionResult UpdateRetryConfig([FromBody] RetryConfig config)
    {
        if (config.RetryDelaySeconds < 0)
        {
            return BadRequest("Retry delay must be non-negative");
        }

        _providersConfig.RetryDelaySeconds = config.RetryDelaySeconds;

        return Ok(new RetryConfig { RetryDelaySeconds = _providersConfig.RetryDelaySeconds });
    }

    private static Dictionary<string, CapabilityConfig>? MapCapabilities(List<CapabilityInfo>? capabilities)
    {
        if (capabilities == null || capabilities.Count == 0)
            return null;

        return capabilities.ToDictionary(
            c => c.Name ?? string.Empty,
            c => new CapabilityConfig(
                Name: c.Name ?? string.Empty,
                Cost: c.Cost,
                Complexity: c.Complexity,
                Supported: c.Supported,
                MaxTokens: c.MaxTokens,
                MaxContexts: c.MaxContexts,
                Limits: c.Limits,
                Features: c.Features));
    }
}

public record ProviderInfo
{
    public string? Name { get; set; }
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public int Priority { get; set; }
    public string? Model { get; set; }
    public bool? SendMaxCompletionTokens { get; set; }
    public int Cost { get; set; }
    public int Complexity { get; set; }
    public string? Description { get; set; }
    public List<CapabilityInfo>? Capabilities { get; set; }
    public bool? HasAllCapabilities { get; set; }
    public bool? AllowAutomatic { get; set; }
}

public record CapabilityInfo
{
    public string? Name { get; set; }
    public int Cost { get; set; }
    public int Complexity { get; set; }
    public bool Supported { get; set; }
    public int? MaxTokens { get; set; }
    public int? MaxContexts { get; set; }
    public Dictionary<string, object>? Limits { get; set; }
    public Dictionary<string, object>? Features { get; set; }
}

public record ProvidersList
{
    public List<ProviderInfo> Providers { get; set; } = new();
    public int RetryDelaySeconds { get; set; }
}

public record RetryConfig
{
    public int RetryDelaySeconds { get; set; }
}
