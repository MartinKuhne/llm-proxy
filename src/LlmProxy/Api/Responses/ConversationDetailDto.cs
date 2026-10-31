namespace LlmProxy.Api.Responses;

/// <summary>
/// One conversation with the full replay of its events.
/// </summary>
public class ConversationDetailDto
{
    public string Id { get; set; } = string.Empty;
    public string? Title { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public int TurnCount { get; set; }
    public int EventCount { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public long TotalTokens { get; set; }
    public bool HasErrors { get; set; }
    public List<ConversationEventDto> Events { get; set; } = new();
}
