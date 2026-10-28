namespace LlmProxy.Application.Services;

using LlmProxy.Domain;

/// <summary>
/// Records prompts, responses, tool calls, reasoning content and failures in the conversation audit log.
/// </summary>
/// <remarks>
/// Implementations are fire-and-forget safe: failures are logged, never thrown back to the proxy path.
/// </remarks>
public interface IConversationLogger
{
    /// <summary>Logs an incoming request, including any tool results it carries.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task LogRequestAsync(ConversationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Logs a completed response, including thinking content and tool calls.</summary>
    /// <param name="response">The completed response.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task LogResponseAsync(ConversationResponse response, CancellationToken cancellationToken = default);

    /// <summary>Logs an upstream failure.</summary>
    /// <param name="failure">The failure to log.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task LogFailureAsync(ConversationFailure failure, CancellationToken cancellationToken = default);
}
