namespace LlmProxy.Domain;

using System.Text.Json;

/// <summary>
/// A single audited event in a conversation. One JSON line per event is written to the conversation file.
/// </summary>
/// <param name="Kind">One of the <see cref="ConversationEventKind"/> values.</param>
/// <param name="Timestamp">When the event happened.</param>
/// <param name="ConversationId">Conversation this event belongs to.</param>
/// <param name="RequestId">Request within the conversation that produced the event.</param>
/// <param name="Provider">Provider the request was sent to, when known.</param>
/// <param name="Model">Model that handled the request, when known.</param>
/// <param name="Path">Request path, when known.</param>
/// <param name="Data">Kind-specific payload kept as raw JSON so upstream shapes round-trip losslessly.</param>
public sealed record ConversationEvent(
    string Kind,
    DateTimeOffset Timestamp,
    string ConversationId,
    string RequestId,
    string? Provider,
    string? Model,
    string? Path,
    JsonElement? Data);
