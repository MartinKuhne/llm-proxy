namespace GreedyLlm.Domain;

public record Usage(int PromptTokens, int CompletionTokens, int TotalTokens);
