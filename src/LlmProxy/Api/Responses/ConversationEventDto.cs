namespace LlmProxy.Api.Responses;

using System.Text.Json;

/// <summary>
/// One audited event of a conversation. The kind-specific payload is passed through untouched.
/// </summary>
public class ConversationEventDto
{
    public string Kind { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? Path { get; set; }
    public JsonElement? Data { get; set; }
}
