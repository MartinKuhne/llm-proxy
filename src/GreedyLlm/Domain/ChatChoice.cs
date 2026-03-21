namespace GreedyLlm.Domain;

public record ChatChoice(int Index, ChatMessage Message, string FinishReason);
