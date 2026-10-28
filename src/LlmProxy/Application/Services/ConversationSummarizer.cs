namespace LlmProxy.Application.Services;

using System.Text.Json;
using LlmProxy.Domain;

/// <summary>
/// Folds <see cref="ConversationEvent"/>s into the <see cref="ConversationSummary"/> roll-up of their conversation.
/// </summary>
/// <remarks>
/// Shared by the index (live recording and rebuild) and the reader (summary fallback for a conversation the index
/// does not know), so both derive the same numbers from the same events.
/// </remarks>
internal static class ConversationSummarizer
{
    private const int MaxTitleLength = 80;

    /// <summary>Builds the summary of a conversation by folding all of its events.</summary>
    /// <param name="conversationId">Conversation the events belong to.</param>
    /// <param name="events">Events to fold, in any order.</param>
    internal static ConversationSummary Summarize(string conversationId, IEnumerable<ConversationEvent> events)
    {
        var summary = new ConversationSummary { Id = conversationId };
        foreach (var evt in events)
        {
            Fold(summary, evt);
        }

        return summary;
    }

    /// <summary>Applies one event to a summary: dates, counts, title, provider, model, tokens and the error flag.</summary>
    /// <param name="summary">Summary to update.</param>
    /// <param name="evt">Event to apply.</param>
    internal static void Fold(ConversationSummary summary, ConversationEvent evt)
    {
        if (summary.StartedAt == default || evt.Timestamp < summary.StartedAt)
        {
            summary.StartedAt = evt.Timestamp;
        }

        if (evt.Timestamp > summary.LastActivityAt)
        {
            summary.LastActivityAt = evt.Timestamp;
        }

        summary.EventCount++;

        if (evt.Kind == ConversationEventKind.Request)
        {
            summary.TurnCount++;
            FoldRecordedState(summary, evt);
            if (string.IsNullOrEmpty(summary.Title))
            {
                summary.Title = TitleFrom(evt);
            }
        }

        if (evt.Kind == ConversationEventKind.Error)
        {
            summary.HasErrors = true;
        }

        if (string.IsNullOrEmpty(summary.Provider) && !string.IsNullOrEmpty(evt.Provider))
        {
            summary.Provider = evt.Provider;
        }

        if (string.IsNullOrEmpty(summary.Model) && !string.IsNullOrEmpty(evt.Model))
        {
            summary.Model = evt.Model;
        }

        summary.TotalTokens += TotalTokensFrom(evt);
    }

    /// <summary>
    /// Carries the dedupe state a request event reports into the summary, so the next turn of the conversation can
    /// tell what is already stored. Fields the event does not carry leave the summary untouched: an omitted tools
    /// array means "still the one already stored", not "none".
    /// </summary>
    private static void FoldRecordedState(ConversationSummary summary, ConversationEvent evt)
    {
        if (evt.Data is not { } data || data.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (data.TryGetProperty("newMessageCount", out var newMessageCount) &&
            newMessageCount.TryGetInt32(out var added) && added > 0)
        {
            summary.RecordedMessageCount += added;
        }

        if (TryGetString(data, "lastMessageFingerprint", out var lastFingerprint))
        {
            summary.LastRecordedMessageFingerprint = lastFingerprint;
        }

        if (TryGetString(data, "toolsFingerprint", out var toolsFingerprint))
        {
            summary.RecordedToolsFingerprint = toolsFingerprint;
        }
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return value is not null;
        }

        value = null;
        return false;
    }

    private static long TotalTokensFrom(ConversationEvent evt)
    {
        if (evt.Kind != ConversationEventKind.Response || evt.Data is not { } data)
        {
            return 0;
        }

        if (!TryGetObject(data, "usage", out var usage) || !usage.TryGetProperty("total_tokens", out var totalTokens))
        {
            return 0;
        }

        return totalTokens.TryGetInt64(out var value) && value > 0 ? value : 0;
    }

    private static string? TitleFrom(ConversationEvent evt)
    {
        if (evt.Data is not { } data)
        {
            return null;
        }

        // Current events carry the new messages at the top level; older events kept them inside the body.
        JsonElement messages;
        if (TryGetObjectArray(data, "messages", out var top))
        {
            messages = top;
        }
        else if (TryGetObject(data, "body", out var body) && TryGetObjectArray(body, "messages", out var nested))
        {
            messages = nested;
        }
        else
        {
            return null;
        }

        foreach (var message in messages.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object ||
                !message.TryGetProperty("role", out var role) ||
                role.ValueKind != JsonValueKind.String ||
                role.GetString() != "user")
            {
                continue;
            }

            var title = TitleFromMessage(message);
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
        }

        return null;
    }

    private static string? TitleFromMessage(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return Preview(content.GetString());
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var part in content.EnumerateArray())
        {
            string? text = null;
            if (part.ValueKind == JsonValueKind.String)
            {
                text = part.GetString();
            }
            else if (part.ValueKind == JsonValueKind.Object &&
                     part.TryGetProperty("text", out var textProperty) &&
                     textProperty.ValueKind == JsonValueKind.String)
            {
                text = textProperty.GetString();
            }

            var preview = Preview(text);
            if (!string.IsNullOrWhiteSpace(preview))
            {
                return preview;
            }
        }

        return null;
    }

    private static string? Preview(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var collapsed = value.ReplaceLineEndings(" ").Trim();
        return collapsed.Length <= MaxTitleLength ? collapsed : collapsed[..MaxTitleLength];
    }

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetObjectArray(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out value) &&
            value.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        value = default;
        return false;
    }
}
