namespace LlmProxy.Domain;

public record TokenCountLog(
    DateTime Date,
    string Provider,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    int CachedTokens,
    int RequestCount,
    long ElapsedMs);
