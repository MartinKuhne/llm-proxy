namespace GreedyLlm.Api.Responses;

public class ChatCompletionResponseDto
{
    public string Id { get; set; } = string.Empty;
    public string Object { get; set; } = string.Empty;
    public long Created { get; set; }
    public string Model { get; set; } = string.Empty;
    public IEnumerable<ChatChoiceDto> Choices { get; set; } = new List<ChatChoiceDto>();
    public UsageDto Usage { get; set; } = new();
}
