namespace LlmProxy.Domain;

public enum ProviderErrorType
{
    Unknown,
    ContextLengthExceeded,
    MaxTokensExceeded,
    TokenLimitExceeded,
    StringTooLong,
    Authentication,
    PermissionDenied,
    PaymentRequired,
    RateLimitExceeded,
    ProviderOverloaded,
    ProviderUnavailable,
    InvalidRequest,
    InvalidPrompt,
    NotFound,
    PreconditionFailed,
    PayloadTooLarge,
    Unprocessable,
    ContentPolicyViolation,
    Refusal,
    InvalidImage,
    ImageTooLarge,
    ImageTooSmall,
    UnsupportedImageFormat,
    ImageNotFound,
    ImageDownloadFailed,
    Server,
    Timeout,
    Unmapped
}
