namespace LlmProxy.Api.Controllers;

using LlmProxy.Application.Services;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Catch-all transparent proxy for all /v1/** paths not handled by more specific controllers.
/// Specific routes (chat/completions, models) take precedence due to ASP.NET Core's
/// route specificity ordering (literals > parameters > catch-all).
/// </summary>
[ApiController]
[Route("v1")]
public class ProxyController : ControllerBase
{
    private readonly IProxyService _proxyService;

    public ProxyController(IProxyService proxyService)
    {
        _proxyService = proxyService;
    }

    [HttpGet("{**catchAll}")]
    [HttpPost("{**catchAll}")]
    [HttpPut("{**catchAll}")]
    [HttpPatch("{**catchAll}")]
    [HttpDelete("{**catchAll}")]
    public async Task ProxyAsync(string catchAll, CancellationToken cancellationToken = default)
    {
        await _proxyService.ProxyAsync(HttpContext, catchAll, cancellationToken);
    }
}
