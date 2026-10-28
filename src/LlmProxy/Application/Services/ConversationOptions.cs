namespace LlmProxy.Application.Services;

/// <summary>
/// Settings for the conversation audit log, bound from the "Conversations" configuration section.
/// </summary>
public class ConversationOptions
{
    /// <summary>Turns conversation auditing on or off.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Target directory; null means <see cref="AppPaths.ConversationDirectory"/>.</summary>
    public string? Directory { get; set; }

    /// <summary>Largest size kept for any single string field; longer strings are truncated.</summary>
    public int MaxFieldChars { get; set; } = 100_000;
}
