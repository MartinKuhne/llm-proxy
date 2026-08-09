namespace LlmProxy.Domain;

public record Usage(int PromptTokens, int CompletionTokens, int TotalTokens, int CachedTokens = 0);
