using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LlmProxy.Application.Configuration;
using LlmProxy.Domain;
using Microsoft.Extensions.Logging;

using System.Text.RegularExpressions;

namespace LlmProxy.Application.Services;

/// <summary>
/// Service implementation for loading free models from OpenRouter.
/// </summary>
public class OpenRouterModelLoader(IHttpClientFactory httpClientFactory, ILogger<OpenRouterModelLoader> logger) : IOpenRouterModelLoader
{
    private const string OpenRouterModelsUrl = "https://openrouter.ai/api/v1/models";
    private static readonly Regex ModelSizeRegex = new(@"(\d+)[bB]", RegexOptions.Compiled);

    /// <summary>
    /// Loads free models from OpenRouter and appends them to the provider configuration.
    /// </summary>
    /// <param name="config">The providers configuration to update.</param>
    /// <param name="cancellationToken">Cancellation token to propagate.</param>
    /// <returns>A task that represents the asynchronous loading operation.</returns>
    public async Task LoadFreeModelsAsync(ProvidersConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Fetching models from OpenRouter API: {Url}", OpenRouterModelsUrl);

            using var httpClient = httpClientFactory.CreateClient();
            var response = await httpClient.GetAsync(OpenRouterModelsUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OpenRouterModelsResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);

            if (result?.Data == null || result.Data.Count == 0)
            {
                logger.LogWarning("No models retrieved from OpenRouter API.");
                return;
            }

            var freeModels = result.Data
                .Where(IsFreeModel)
                .Where(m => !string.IsNullOrEmpty(m.Id))
                .GroupBy(m => m.Id)
                .Select(g => g.First())
                .ToList();

            logger.LogInformation("Found {Count} free models out of {TotalCount} total OpenRouter models.", freeModels.Count, result.Data.Count);

            var apiKey = config.OpenRouterApiKey ?? "";
            var openRouterProviders = new List<ProviderConfig>();
            
            var existingKeys = config.Providers
                .Select(p => p.ProviderKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var model in freeModels)
            {
                var modelId = model.Id!;
                var baseUrl = "https://openrouter.ai/api/v1";
                var key = new ProviderConfig("OpenRouter", baseUrl, "", 0, modelId).ProviderKey;
                if (existingKeys.Contains(key))
                {
                    continue;
                }

                var modelName = model.Name ?? "";
                int priority = 500;

                var match = ModelSizeRegex.Match(modelName);
                if (!match.Success)
                {
                    match = ModelSizeRegex.Match(modelId);
                }

                if (match.Success && int.TryParse(match.Groups[1].Value, out int sizeValue))
                {
                    priority += sizeValue;
                }

                var providerConfig = new ProviderConfig(
                    Name: "OpenRouter",
                    BaseUrl: baseUrl,
                    ApiKey: apiKey,
                    Priority: priority,
                    Model: modelId,
                    SendMaxCompletionTokens: false,
                    Cost: 0,
                    Complexity: 0,
                    Description: model.Description ?? $"OpenRouter free model: {modelName}",
                    Capabilities: null,
                    HasAllCapabilities: true,
                    AllowAutomatic: true
                );

                openRouterProviders.Add(providerConfig);
                existingKeys.Add(key);
            }

            var currentList = config.Providers.ToList();
            currentList.AddRange(openRouterProviders);
            config.Providers = currentList;

            foreach (var provider in openRouterProviders)
            {
                logger.LogInformation("Added OpenRouter free model: {Model} (Priority: {Priority})", provider.Model, provider.Priority);
            }

            logger.LogInformation("Successfully appended {Count} OpenRouter free models to the provider list.", openRouterProviders.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while loading OpenRouter models.");
        }
    }

    private static bool IsFreeModel(OpenRouterModel model)
    {
        if (model.Pricing == null || model.Pricing.Count == 0)
        {
            return false;
        }

        return model.Pricing.Values.All(v =>
            (v.ValueKind == JsonValueKind.Number && v.GetDouble() == 0) ||
            (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out double val) && val == 0)
        );
    }

    private class OpenRouterModelsResponse
    {
        public List<OpenRouterModel>? Data { get; set; }
    }

    private class OpenRouterModel
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public Dictionary<string, JsonElement>? Pricing { get; set; }
    }
}
