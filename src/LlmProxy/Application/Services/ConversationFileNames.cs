namespace LlmProxy.Application.Services;

using System.Text;

/// <summary>
/// Owns the on-disk naming of conversation files so the writer, the index and the reader cannot diverge.
/// </summary>
internal static class ConversationFileNames
{
    /// <summary>Extension every conversation file carries.</summary>
    internal const string Extension = ".jsonl";

    private const int MaxFileNameLength = 100;

    /// <summary>Makes a conversation id safe to use as a file name: no separators, no traversal, capped length.</summary>
    /// <param name="conversationId">Raw conversation id.</param>
    internal static string FileName(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return "_";
        }

        var builder = new StringBuilder(Math.Min(conversationId.Length, MaxFileNameLength));
        foreach (var character in conversationId)
        {
            if (builder.Length >= MaxFileNameLength)
            {
                break;
            }

            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_');
        }

        var name = builder.ToString().Replace("..", "._").TrimEnd('.');
        return name.Length > 0 ? name : "_";
    }

    /// <summary>Full path of a conversation file inside the configured conversation directory.</summary>
    /// <param name="conversationId">Conversation to locate.</param>
    internal static string PathFor(string conversationId) => PathFor(AppPaths.ConversationDirectory, conversationId);

    /// <summary>Full path of a conversation file inside an explicit directory.</summary>
    /// <param name="directory">Directory holding the conversation files.</param>
    /// <param name="conversationId">Conversation to locate.</param>
    internal static string PathFor(string directory, string conversationId) =>
        Path.Combine(directory, FileName(conversationId) + Extension);
}
