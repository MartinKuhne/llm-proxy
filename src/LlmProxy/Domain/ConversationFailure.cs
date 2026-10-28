namespace LlmProxy.Domain;

/// <summary>
/// An upstream failure to be written to the conversation audit log.
/// </summary>
/// <param name="ConversationId">Conversation the failure belongs to.</param>
/// <param name="RequestId">Request identifier within the conversation.</param>
/// <param name="Timestamp">When the failure happened.</param>
/// <param name="Provider">Provider that failed, when known.</param>
/// <param name="Model">Model involved, when known.</param>
/// <param name="Path">Request path, when known.</param>
/// <param name="StatusCode">HTTP status code of the failure, when one was received.</param>
/// <param name="Body">Error body or exception message, when available.</param>
public sealed record ConversationFailure(
    string ConversationId,
    string RequestId,
    DateTimeOffset Timestamp,
    string? Provider,
    string? Model,
    string? Path,
    int? StatusCode,
    string? Body);
