namespace LlmProxy.Domain;

using System;
using System.Collections.Generic;
using System.Text.Json;

public record ProviderError(
    int? Code,
    string Message,
    ProviderErrorType ErrorType,
    string? ProviderCode,
    Dictionary<string, JsonElement>? Metadata)
{
    public static ProviderError FromException(Exception ex, string? message = null)
    {
        var errorType = ProviderErrorType.Unknown;
        if (ex is OperationCanceledException || ex is TimeoutException)
        {
            errorType = ProviderErrorType.Timeout;
        }

        return new ProviderError(
            null,
            message ?? ex.Message,
            errorType,
            null,
            null);
    }
}
