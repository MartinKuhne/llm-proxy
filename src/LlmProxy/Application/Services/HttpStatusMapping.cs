namespace LlmProxy.Application.Services;

using LlmProxy.Domain;

/// <summary>
/// Shared mapping from HTTP status codes to provider error types and default messages.
/// Used by both DefaultErrorParser and OpenRouterErrorParser.
/// </summary>
internal static class HttpStatusMapping
{
    /// <summary>
    /// Maps an HTTP status code to a <see cref="ProviderErrorType"/>.
    /// </summary>
    public static ProviderErrorType MapErrorType(int statusCode) => statusCode switch
    {
        400 => ProviderErrorType.InvalidRequest,
        401 => ProviderErrorType.Authentication,
        402 => ProviderErrorType.PaymentRequired,
        403 => ProviderErrorType.PermissionDenied,
        404 => ProviderErrorType.NotFound,
        408 => ProviderErrorType.Timeout,
        412 => ProviderErrorType.PreconditionFailed,
        413 => ProviderErrorType.PayloadTooLarge,
        422 => ProviderErrorType.Unprocessable,
        429 => ProviderErrorType.RateLimitExceeded,
        500 => ProviderErrorType.Server,
        502 => ProviderErrorType.ProviderUnavailable,
        503 => ProviderErrorType.ProviderOverloaded,
        504 => ProviderErrorType.Timeout,
        _ => ProviderErrorType.Unknown
    };

    /// <summary>
    /// Returns a human-readable default message for an HTTP status code.
    /// </summary>
    public static string GetDefaultMessage(int statusCode) => statusCode switch
    {
        400 => "Bad request",
        401 => "Unauthorized",
        402 => "Payment required",
        403 => "Forbidden",
        404 => "Not found",
        408 => "Request timeout",
        412 => "Precondition failed",
        413 => "Payload too large",
        422 => "Unprocessable entity",
        429 => "Rate limit exceeded",
        500 => "Internal server error",
        502 => "Bad gateway",
        503 => "Service unavailable",
        504 => "Gateway timeout",
        _ => $"Upstream returned HTTP {statusCode}"
    };
}
