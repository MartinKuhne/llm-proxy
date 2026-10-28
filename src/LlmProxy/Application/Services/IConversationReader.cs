namespace LlmProxy.Application.Services;

/// <summary>
/// Reads the conversation audit log back for the operator surface.
/// </summary>
public interface IConversationReader
{
    /// <summary>Lists conversations matching the query, most recently active first.</summary>
    /// <param name="query">Filter and paging criteria.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<ConversationPage> ListAsync(ConversationQuery query, CancellationToken cancellationToken = default);

    /// <summary>Reads one conversation with its full event replay.</summary>
    /// <param name="conversationId">Conversation to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The conversation, or null when it is unknown or its file is missing.</returns>
    Task<ConversationDetail?> GetAsync(string conversationId, CancellationToken cancellationToken = default);
}
