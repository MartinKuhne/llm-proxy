using System.Threading;
using System.Threading.Tasks;
using LlmProxy.Application.Configuration;

namespace LlmProxy.Application.Services;

/// <summary>
/// Service interface for loading models from OpenRouter on startup.
/// </summary>
public interface IOpenRouterModelLoader
{
    /// <summary>
    /// Loads free models from OpenRouter and appends them to the provider configuration.
    /// </summary>
    /// <param name="config">The providers configuration to update.</param>
    /// <param name="cancellationToken">Cancellation token to propagate.</param>
    /// <returns>A task that represents the asynchronous loading operation.</returns>
    Task LoadFreeModelsAsync(ProvidersConfig config, CancellationToken cancellationToken = default);
}
