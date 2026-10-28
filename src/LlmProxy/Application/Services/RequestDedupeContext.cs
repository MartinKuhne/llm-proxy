namespace LlmProxy.Application.Services;

/// <summary>
/// What the audit log already holds for a conversation, so a resent request writes only the part that is new.
/// </summary>
/// <param name="RecordedMessageCount">How many request messages are already stored for this conversation.</param>
/// <param name="LastRecordedMessageFingerprint">Fingerprint of the last stored message, null when none is stored.</param>
/// <param name="RecordedToolsFingerprint">Fingerprint of the tools array already stored, null when none is stored.</param>
internal sealed record RequestDedupeContext(
    int RecordedMessageCount,
    string? LastRecordedMessageFingerprint,
    string? RecordedToolsFingerprint)
{
    /// <summary>Context of a conversation nothing has been recorded for yet.</summary>
    internal static readonly RequestDedupeContext Empty = new(0, null, null);
}
