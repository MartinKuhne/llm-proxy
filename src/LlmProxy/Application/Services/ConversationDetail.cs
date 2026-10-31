namespace LlmProxy.Application.Services;

using LlmProxy.Domain;

/// <summary>
/// One conversation: its summary together with every event recorded for it, in file order.
/// </summary>
/// <param name="Id">Conversation identifier.</param>
/// <param name="Title">Short human label derived from the first user message.</param>
/// <param name="StartedAt">When the first event of the conversation was seen.</param>
/// <param name="LastActivityAt">When the most recent event of the conversation was seen.</param>
/// <param name="TurnCount">Number of request turns recorded.</param>
/// <param name="EventCount">Number of events recorded.</param>
/// <param name="Provider">First provider seen.</param>
/// <param name="Model">First model seen.</param>
/// <param name="TotalTokens">Total tokens reported by upstream usage objects.</param>
/// <param name="HasErrors">True when at least one error event was recorded.</param>
/// <param name="Events">Every event of the conversation, oldest first.</param>
public sealed record ConversationDetail(
    string Id,
    string? Title,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt,
    int TurnCount,
    int EventCount,
    string? Provider,
    string? Model,
    long TotalTokens,
    bool HasErrors,
    IReadOnlyList<ConversationEvent> Events);
