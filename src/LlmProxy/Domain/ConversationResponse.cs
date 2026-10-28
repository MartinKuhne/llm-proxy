namespace LlmProxy.Domain;

using System.Text.Json;

/// <summary>
/// A completed response to be written to the conversation audit log.
/// </summary>
/// <param name="ConversationId">Conversation the response belongs to.</param>
/// <param name="RequestId">Request identifier within the conversation.</param>
/// <param name="Timestamp">When the response completed.</param>
/// <param name="Provider">Provider that answered, when known.</param>
/// <param name="Model">Model that answered, when known.</param>
/// <param name="Path">Request path, when known.</param>
/// <param name="AssistantMessage">
/// Assistant message object. May contain <c>content</c>, <c>reasoning</c>/<c>reasoning_content</c>,
/// <c>tool_calls</c> and <c>finish_reason</c>.
/// </param>
/// <param name="Usage">OpenAI usage object, when the upstream returned one.</param>
/// <param name="Streamed">True when the response was streamed to the client.</param>
public sealed record ConversationResponse(
    string ConversationId,
    string RequestId,
    DateTimeOffset Timestamp,
    string? Provider,
    string? Model,
    string? Path,
    JsonElement? AssistantMessage,
    JsonElement? Usage,
    bool Streamed);
