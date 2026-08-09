namespace LlmProxy.Api.Controllers;

using LlmProxy.Application.Services;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Controller for managing models in provider:model format.
/// </summary>
[ApiController]
[Route("v1/models")]
public class ModelsController : ControllerBase
{
    private readonly IProviderManager _providerManager;

    public ModelsController(IProviderManager providerManager)
    {
        _providerManager = providerManager;
    }

    [HttpGet]
    /// <summary>
    /// Returns the list of available models in provider:model format.
    /// </summary>
    public IActionResult GetModels()
    {
        var createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var models = _providerManager.GetAllProviders()
            .Select(p => new 
            { 
                id = $"{p.Name}:{p.Model}",
                @object = "model",
                created = createdTimestamp,
                owned_by = "llm-proxy"
            })
            .ToList();
        
        models.Insert(0, new 
        { 
            id = "llm-proxy:auto",
            @object = "model",
            created = createdTimestamp,
            owned_by = "llm-proxy"
        });
        
        return Ok(new { @object = "list", data = models });
    }
}
