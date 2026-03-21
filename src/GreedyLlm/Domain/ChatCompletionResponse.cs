namespace GreedyLlm.Domain;

public record ChatCompletionResponse(
    string Id,
    string Object,
    long Created,
    string Model,
    IEnumerable<ChatChoice> Choices,
    Usage Usage);
