namespace LlmProxy.Application.Services;

using System.Text.Json;
using LlmProxy.Domain;

/// <summary>
/// Parses upstream provider errors by inferring the error type from the HTTP status
/// code and extracting a human-readable message from a JSON body if present.
/// Used as the fallback for providers that don't have a specialised parser.
/// </summary>
public class DefaultErrorParser : IProviderErrorParser
{
    public ProviderError Parse(int statusCode, string? responseBody)
    {
        var message = HttpStatusMapping.GetDefaultMessage(statusCode);

        if (responseBody != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("error", out var errorElement))
                {
                    if (errorElement.ValueKind == JsonValueKind.Object
                        && errorElement.TryGetProperty("message", out var msgProp))
                    {
                        message = msgProp.GetString() ?? message;
                    }
                    else if (errorElement.ValueKind == JsonValueKind.String)
                    {
                        message = errorElement.GetString() ?? message;
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return new ProviderError(statusCode, message, HttpStatusMapping.MapErrorType(statusCode), null, null);
    }
}
