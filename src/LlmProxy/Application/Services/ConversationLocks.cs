namespace LlmProxy.Application.Services;

using System.Collections.Concurrent;

/// <summary>
/// Hands out one <see cref="SemaphoreSlim"/> per conversation file, so the write sequence of a single conversation
/// is atomic while different conversations keep writing concurrently.
/// </summary>
/// <remarks>
/// Keyed on the sanitized file name rather than the raw conversation id: two ids that map onto the same file have
/// to share one lock, otherwise they would append to it at the same time.
/// </remarks>
public sealed class ConversationLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _byFileName = new(StringComparer.Ordinal);

    /// <summary>The lock that guards the conversation file the given id maps to.</summary>
    /// <param name="conversationId">Conversation to lock.</param>
    public SemaphoreSlim For(string conversationId) =>
        _byFileName.GetOrAdd(ConversationFileNames.FileName(conversationId), _ => new SemaphoreSlim(1, 1));
}
