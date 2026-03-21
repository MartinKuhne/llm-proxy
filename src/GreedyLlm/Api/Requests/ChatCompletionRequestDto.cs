namespace GreedyLlm.Api.Requests;

public class ChatCompletionRequestDto
{
    public string Model { get; set; } = string.Empty;
    public IEnumerable<ChatMessageDto> Messages { get; set; } = new List<ChatMessageDto>();
    public bool Stream { get; set; }
}
