namespace LlmProxy.Application.Services;

using LlmProxy.Application.Configuration;
using LlmProxy.Domain;
using System.Collections.Concurrent;

public class ProviderManager : IProviderManager
{
    private readonly IEnumerable<ProviderConfig> _providers;
    private readonly ConcurrentDictionary<string, ProviderState> _providerStates;
    private volatile ProviderConfig? _forcedProvider;

    public ProviderManager(ProvidersConfig config)
    {
        _providers = config.Providers.OrderByDescending(p => p.Priority).ToList();
        _providerStates = new ConcurrentDictionary<string, ProviderState>(
            _providers.ToDictionary(
                p => p.ProviderKey,
                _ => new ProviderState(
                    LastUsed: DateTime.MinValue,
                    LastError: null,
                    ErrorCount: 0,
                    LastErrorMessage: null,
                    LastException: null,
                    LastErrorStatusCode: null,
                    LastErrorBody: null,
                    LastErrorType: null,
                    LastProviderCode: null)),
            StringComparer.OrdinalIgnoreCase);
    }

    public ProviderState? GetProviderState(ProviderConfig provider)
        => _providerStates.GetValueOrDefault(provider.ProviderKey);

    public void RecordSuccess(ProviderConfig provider)
    {
        _providerStates.AddOrUpdate(
            provider.ProviderKey,
            _ => new ProviderState(DateTime.UtcNow, null, 0, null, null, null, null, null, null),
            (_, existing) => existing with
            {
                LastUsed = DateTime.UtcNow,
                LastError = null,
                ErrorCount = 0,
                LastErrorMessage = null,
                LastException = null,
                LastErrorStatusCode = null,
                LastErrorBody = null,
                LastErrorType = null,
                LastProviderCode = null
            });
    }

    private static readonly DefaultErrorParser _fallbackParser = new();

    public void RecordError(ProviderConfig provider, string errorMessage, Exception? exception = null, int? statusCode = null, string? responseBody = null)
    {
        var error = exception != null
            ? ProviderError.FromException(exception, errorMessage)
            : statusCode.HasValue
                ? _fallbackParser.Parse(statusCode.Value, responseBody)
                : new ProviderError(null, errorMessage, ProviderErrorType.Unknown, null, null);

        RecordError(provider, error with { Message = errorMessage });
    }

    public void RecordError(ProviderConfig provider, ProviderError error)
    {
        ProviderState? existingState = null;
        _providerStates.AddOrUpdate(
            provider.ProviderKey,
            _ => new ProviderState(
                DateTime.UtcNow, DateTime.UtcNow, 1,
                error.Message, null, error.Code,
                TruncateBody(null),
                error.ErrorType, error.ProviderCode),
            (_, existing) =>
            {
                existingState = existing;
                return existing with
                {
                    LastUsed = DateTime.UtcNow,
                    LastError = DateTime.UtcNow,
                    ErrorCount = existing.ErrorCount + 1,
                    LastErrorMessage = error.Message,
                    LastException = null,
                    LastErrorStatusCode = error.Code,
                    LastErrorBody = TruncateBody(null),
                    LastErrorType = error.ErrorType,
                    LastProviderCode = error.ProviderCode
                };
            });

        if (provider == _forcedProvider && existingState?.ErrorCount >= 2)
        {
            SetForcedProvider(null);
        }
    }

    private static string? TruncateBody(string? body)
    {
        if (body == null) return null;
        return body.Length > 500 ? body.Substring(0, 500) + "...[truncated]" : body;
    }

    public ProviderConfig? GetForcedProvider() => _forcedProvider;

    public void SetForcedProvider(string? providerKey)
    {
        _forcedProvider = providerKey == null
            ? null
            : _providers.FirstOrDefault(p => p.ProviderKey == providerKey || p.FullModelKey == providerKey);
    }



    public ProviderConfig? GetProviderForCapability(string capability, int? maxCost = null, int? maxComplexity = null)
    {
        var capableProviders = GetCapableProviders(capability)
            .Where(IsProviderAvailable)
            .Where(p =>
            {
                if (maxCost.HasValue && p.Cost > maxCost.Value) return false;
                if (maxComplexity.HasValue && p.Complexity > maxComplexity.Value) return false;
                return true;
            })
            .OrderByDescending(p => p.Priority)
            .ToList();

        foreach (var provider in capableProviders)
        {
            if (provider.HasAllCapabilities || ProviderSupportsCapability(provider, capability))
            {
                return provider;
            }
        }

        return null;
    }

    public IEnumerable<ProviderConfig> GetCapableProviders(string capability)
    {
        return _providers.Where(p => ProviderSupportsCapability(p, capability))
                         .OrderByDescending(p => p.Priority)
                         .ToList();
    }

    public bool ProviderSupportsCapability(ProviderConfig provider, string capability)
    {
        if (provider.HasAllCapabilities) return true;
        
        if (provider.Capabilities == null) return false;
        
        return provider.Capabilities.ContainsKey(capability) && 
               provider.Capabilities[capability].Supported;
    }

    public CapabilityConfig GetCapabilityConfig(ProviderConfig provider, string capability)
    {
        if (provider.HasAllCapabilities) 
            return new CapabilityConfig(capability, provider.Cost, provider.Complexity, true);
        
        if (provider.Capabilities == null || !provider.Capabilities.ContainsKey(capability))
            return new CapabilityConfig(capability, 0, 0, false);
        
        return provider.Capabilities[capability];
    }

    public ProviderConfig? GetBestProvider()
    {
        if (_forcedProvider != null)
            return _forcedProvider;

        return _providers
            .Where(IsProviderAvailable)
            .OrderByDescending(p => p.Priority)
            .FirstOrDefault();
    }

    /// <summary>
    /// Returns true if the provider has no recent errors (within the recovery window).
    /// </summary>
    private bool IsProviderAvailable(ProviderConfig provider)
    {
        var state = _providerStates.GetValueOrDefault(provider.ProviderKey);
        return state == null || state.ErrorCount == 0 || state.LastError < DateTime.UtcNow.AddHours(-1);
    }

    public IEnumerable<ProviderConfig> GetAllProviders() => _providers;
}
