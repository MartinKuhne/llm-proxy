namespace LlmProxy.Domain;

public record ProviderState(
    DateTime LastUsed,
    DateTime? LastError,
    int ErrorCount,
    string? LastErrorMessage,
    Exception? LastException,
    int? LastErrorStatusCode,
    string? LastErrorBody,
    ProviderErrorType? LastErrorType,
    string? LastProviderCode
);
