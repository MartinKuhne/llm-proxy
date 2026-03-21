namespace GreedyLlm.Api.Responses;

using GreedyLlm.Api.Requests;

public class ChatChoiceDto
{
    public int Index { get; set; }
    public ChatMessageDto Message { get; set; } = new();
    public string FinishReason { get; set; } = string.Empty;
}
