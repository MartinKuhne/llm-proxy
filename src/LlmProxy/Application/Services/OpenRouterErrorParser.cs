namespace LlmProxy.Application.Services;

using System.Text.Json;
using LlmProxy.Domain;

/// <summary>
/// Parses error responses from the OpenRouter API.
///
/// Reference: https://openrouter.ai/docs/api_reference/errors-and-debugging
///
/// OpenRouter returns structured error JSON with a typed error_type field
/// inside error.metadata. This parser maps those types to ProviderErrorType
/// and extracts provider-level error codes when available.
///
/// For non-OpenRouter endpoints it degrades to HTTP-status-based inference
/// via DefaultErrorParser so this implementation is safe to use anywhere.
/// </summary>
public class OpenRouterErrorParser : IProviderErrorParser
{
    private static readonly Dictionary<string, ProviderErrorType> ErrorTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["context_length_exceeded"] = ProviderErrorType.ContextLengthExceeded,
        ["max_tokens_exceeded"] = ProviderErrorType.MaxTokensExceeded,
        ["token_limit_exceeded"] = ProviderErrorType.TokenLimitExceeded,
        ["string_too_long"] = ProviderErrorType.StringTooLong,
        ["authentication"] = ProviderErrorType.Authentication,
        ["permission_denied"] = ProviderErrorType.PermissionDenied,
        ["payment_required"] = ProviderErrorType.PaymentRequired,
        ["rate_limit_exceeded"] = ProviderErrorType.RateLimitExceeded,
        ["provider_overloaded"] = ProviderErrorType.ProviderOverloaded,
        ["provider_unavailable"] = ProviderErrorType.ProviderUnavailable,
        ["invalid_request"] = ProviderErrorType.InvalidRequest,
        ["invalid_prompt"] = ProviderErrorType.InvalidPrompt,
        ["not_found"] = ProviderErrorType.NotFound,
        ["precondition_failed"] = ProviderErrorType.PreconditionFailed,
        ["payload_too_large"] = ProviderErrorType.PayloadTooLarge,
        ["unprocessable"] = ProviderErrorType.Unprocessable,
        ["content_policy_violation"] = ProviderErrorType.ContentPolicyViolation,
        ["refusal"] = ProviderErrorType.Refusal,
        ["invalid_image"] = ProviderErrorType.InvalidImage,
        ["image_too_large"] = ProviderErrorType.ImageTooLarge,
        ["image_too_small"] = ProviderErrorType.ImageTooSmall,
        ["unsupported_image_format"] = ProviderErrorType.UnsupportedImageFormat,
        ["image_not_found"] = ProviderErrorType.ImageNotFound,
        ["image_download_failed"] = ProviderErrorType.ImageDownloadFailed,
        ["server"] = ProviderErrorType.Server,
        ["timeout"] = ProviderErrorType.Timeout,
        ["unmapped"] = ProviderErrorType.Unmapped
    };

    private readonly DefaultErrorParser _fallback = new();

    /// <summary>
    /// Parses an upstream error response. Attempts OpenRouter's structured
    /// { error: { code, message, metadata: { error_type, provider_code } } }
    /// format first; falls back to DefaultErrorParser for anything else.
    /// </summary>
    public ProviderError Parse(int statusCode, string? responseBody)
    {
        if (responseBody == null)
            return _fallback.Parse(statusCode, null);

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("error", out var errorElement)
                || errorElement.ValueKind != JsonValueKind.Object)
            {
                return _fallback.Parse(statusCode, responseBody);
            }

            var code = errorElement.TryGetProperty("code", out var codeProp)
                       && codeProp.ValueKind == JsonValueKind.Number
                ? codeProp.GetInt32()
                : statusCode;

            var message = errorElement.TryGetProperty("message", out var msgProp)
                ? msgProp.GetString()
                : null;

            ProviderErrorType errorType = HttpStatusMapping.MapErrorType(statusCode);
            string? providerCode = null;
            Dictionary<string, JsonElement>? metadata = null;

            if (errorElement.TryGetProperty("metadata", out var metaProp)
                && metaProp.ValueKind == JsonValueKind.Object)
            {
                metadata = [];
                foreach (var kv in metaProp.EnumerateObject())
                    metadata[kv.Name] = kv.Value;

                if (metadata.TryGetValue("error_type", out var etProp)
                    && etProp.ValueKind == JsonValueKind.String
                    && etProp.GetString() is { } etStr
                    && ErrorTypeMap.TryGetValue(etStr, out var mapped))
                {
                    errorType = mapped;
                }

                if (metadata.TryGetValue("provider_code", out var pcProp)
                    && pcProp.ValueKind == JsonValueKind.String)
                {
                    providerCode = pcProp.GetString();
                }
            }

            message ??= HttpStatusMapping.GetDefaultMessage(statusCode);

            return new ProviderError(code, message, errorType, providerCode, metadata);
        }
        catch (JsonException)
        {
            return _fallback.Parse(statusCode, responseBody);
        }
    }
}
