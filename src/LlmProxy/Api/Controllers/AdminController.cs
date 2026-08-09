namespace LlmProxy.Api.Controllers;

using LlmProxy.Api.Requests;
using LlmProxy.Api.Responses;
using LlmProxy.Application.Services;
using LlmProxy.Domain;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("admin")]
public class AdminController : ControllerBase
{
    private readonly IProviderManager _providerManager;
    private readonly IUsageReader _usageReader;

    public AdminController(IProviderManager providerManager, IUsageReader usageReader)
    {
        _providerManager = providerManager;
        _usageReader = usageReader;
    }

    /// <summary>
    /// Returns the admin HTML page.
    /// </summary>
    [HttpGet]
    public ContentResult Index() => Content(AdminHtml, "text/html");

    /// <summary>
    /// Returns the current provider status.
    /// </summary>
    [HttpGet("api/providers")]
    public ProvidersStatusDto GetProviders()
    {
        var forced = _providerManager.GetForcedProvider();
        var auto = forced == null ? _providerManager.GetBestProvider() : null;

        var providers = _providerManager.GetAllProviders().Select(p =>
        {
            var state = _providerManager.GetProviderState(p);
            var capabilities = new Dictionary<string, CapabilityInfoDto>();
            
            if (p.Capabilities != null && p.Capabilities.Count > 0)
            {
                foreach (var kvp in p.Capabilities)
                {
                    capabilities[kvp.Key] = new CapabilityInfoDto(
                        kvp.Value.Name,
                        kvp.Value.Cost,
                        kvp.Value.Complexity,
                        kvp.Value.Supported,
                        kvp.Value.MaxTokens,
                        kvp.Value.MaxContexts,
                        kvp.Value.Limits,
                        kvp.Value.Features);
                }
            }
            else if (p.HasAllCapabilities)
            {
                capabilities["*"] = new CapabilityInfoDto("*", p.Cost, p.Complexity, true);
            }
            
            return new ProviderInfoDto
            {
                Key = p.ProviderKey,
                FullModelKey = p.FullModelKey,
                Name = p.Name,
                BaseUrl = p.BaseUrl,
                Model = p.Model,
                Priority = p.Priority,
                IsForced = p == forced,
                IsAuto = p == auto,
                ErrorCount = state?.ErrorCount ?? 0,
                LastError = state?.LastError,
                LastErrorMessage = state?.LastErrorMessage,
                LastErrorStatusCode = state?.LastErrorStatusCode,
                LastErrorBody = state?.LastErrorBody,
                Cost = p.Cost,
                Complexity = p.Complexity,
                Description = p.Description,
                Capabilities = capabilities
            };
        }).ToList();

        return new ProvidersStatusDto { Providers = providers, IsForced = forced != null };
    }

    /// <summary>
    /// Returns today's provider usage statistics.
    /// </summary>
    [HttpGet("api/usage/today")]
    public async Task<IReadOnlyList<ProviderUsageStatDto>> GetUsageToday(CancellationToken cancellationToken)
    {
        var stats = await _usageReader.ReadTodayAsync(cancellationToken);

        return stats.Select(s =>
        {
            var elapsedSec = s.TotalElapsedMs / 1000.0;
            return new ProviderUsageStatDto
            {
                ProviderName = s.Provider,
                Provider = s.Provider,
                Model = s.Model,
                RequestCount = s.RequestCount,
                PromptTokens = s.PromptTokens,
                CompletionTokens = s.CompletionTokens,
                TotalTokens = s.TotalTokens,
                CachedTokens = s.CachedTokens,
                AvgPromptTokensPerSec = elapsedSec > 0 ? s.PromptTokens / elapsedSec : 0,
                AvgCompletionTokensPerSec = elapsedSec > 0 ? s.CompletionTokens / elapsedSec : 0
            };
        }).ToList();
    }

    /// <summary>
    /// Returns the last 30 days of usage statistics.
    /// </summary>
    [HttpGet("api/usage/last30days")]
    public async Task<IReadOnlyList<DailyUsageStatDto>> GetUsageLast30Days(CancellationToken cancellationToken)
    {
        var stats = await _usageReader.ReadLast30DaysAsync(cancellationToken);

        return stats.Select(s => new DailyUsageStatDto
        {
            Date = s.Date,
            RequestCount = s.RequestCount,
            PromptTokens = s.PromptTokens,
            CompletionTokens = s.CompletionTokens,
            TotalTokens = s.TotalTokens,
            CachedTokens = s.CachedTokens
        }).ToList();
    }

    /// <summary>
    /// Sets the active provider (overrides auto-selection).
    /// </summary>
    [HttpPost("api/providers/active")]
    public IActionResult SetActive([FromBody] SetActiveProviderRequest request)
    {
        _providerManager.SetForcedProvider(request.Key);
        return NoContent();
    }

    /// <summary>
    /// Resets to automatic provider selection.
    /// </summary>
    [HttpDelete("api/providers/active")]
    public IActionResult ResetAuto()
    {
        _providerManager.SetForcedProvider(null);
        return NoContent();
    }

    private static string AdminHtml => System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "admin.html"));
}