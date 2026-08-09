namespace LlmProxy.Domain;

public record ChatCompletionRequest(
    string Model,
    IEnumerable<ChatMessage> Messages,
    bool Stream = false);
