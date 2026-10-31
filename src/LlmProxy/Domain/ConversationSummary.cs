namespace LlmProxy.Domain;

/// <summary>
/// Roll-up of a single conversation, kept in the conversation index and later surfaced by list endpoints.
/// </summary>
public class ConversationSummary
{
    /// <summary>Conversation identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>When the first event of the conversation was seen.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When the most recent event of the conversation was seen.</summary>
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>Number of request turns recorded for the conversation.</summary>
    public int TurnCount { get; set; }

    /// <summary>Number of events recorded for the conversation.</summary>
    public int EventCount { get; set; }

    /// <summary>First provider seen for the conversation.</summary>
    public string? Provider { get; set; }

    /// <summary>First model seen for the conversation.</summary>
    public string? Model { get; set; }

    /// <summary>Short human label derived from the first user message.</summary>
    public string? Title { get; set; }

    /// <summary>Total tokens reported by upstream usage objects.</summary>
    public long TotalTokens { get; set; }

    /// <summary>How many request messages are already stored for this conversation.</summary>
    public int RecordedMessageCount { get; set; }

    /// <summary>Fingerprint of the last request message stored for this conversation, null when none is stored.</summary>
    public string? LastRecordedMessageFingerprint { get; set; }

    /// <summary>Fingerprint of the tools array currently stored for this conversation, null when none is stored.</summary>
    public string? RecordedToolsFingerprint { get; set; }

    /// <summary>True when at least one error event was recorded.</summary>
    public bool HasErrors { get; set; }
}
