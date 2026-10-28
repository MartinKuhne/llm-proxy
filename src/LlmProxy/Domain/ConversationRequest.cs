namespace LlmProxy.Domain;

using System.Text.Json;

/// <summary>
/// An incoming request to be written to the conversation audit log.
/// </summary>
/// <param name="ConversationId">Conversation the request belongs to.</param>
/// <param name="RequestId">Request identifier within the conversation.</param>
/// <param name="Timestamp">When the request was received.</param>
/// <param name="Provider">Provider the request is routed to, when known.</param>
/// <param name="Model">Model the request asks for, when known.</param>
/// <param name="Path">Request path, when known.</param>
/// <param name="Body">Raw JSON request body as received (OpenAI chat-completion shape: messages[], tools[], sampling params).</param>
public sealed record ConversationRequest(
    string ConversationId,
    string RequestId,
    DateTimeOffset Timestamp,
    string? Provider,
    string? Model,
    string? Path,
    JsonElement Body);
