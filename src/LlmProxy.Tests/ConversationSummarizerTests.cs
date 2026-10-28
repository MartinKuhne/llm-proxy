namespace LlmProxy.Tests;

using System.Text.Json;
using LlmProxy.Application.Services;
using LlmProxy.Domain;

/// <summary>
/// Covers the dedupe state <see cref="ConversationSummarizer"/> keeps so a rebuilt index reports exactly what the
/// live recording path reported.
/// </summary>
public class ConversationSummarizerTests
{
    private static readonly DateTimeOffset _start = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fold_RequestEvent_TakesOverTheRecordedState()
    {
        var summary = new ConversationSummary { Id = "conv-1" };

        ConversationSummarizer.Fold(
            summary,
            Request("""{"newMessageCount":2,"lastMessageFingerprint":"aaaabbbbccccdddd","toolsFingerprint":"1111222233334444"}"""));

        Assert.Equal(2, summary.RecordedMessageCount);
        Assert.Equal("aaaabbbbccccdddd", summary.LastRecordedMessageFingerprint);
        Assert.Equal("1111222233334444", summary.RecordedToolsFingerprint);
    }

    [Fact]
    public void Fold_RequestEvents_AccumulateTheRecordedMessageCount()
    {
        var summary = new ConversationSummary { Id = "conv-1" };

        ConversationSummarizer.Fold(summary, Request("""{"newMessageCount":2,"lastMessageFingerprint":"one"}"""));
        ConversationSummarizer.Fold(summary, Request("""{"newMessageCount":3,"lastMessageFingerprint":"two"}"""));

        Assert.Equal(5, summary.RecordedMessageCount);
        Assert.Equal("two", summary.LastRecordedMessageFingerprint);
    }

    [Fact]
    public void Fold_RequestEventWithoutToolsFingerprint_KeepsTheStoredToolsFingerprint()
    {
        var summary = new ConversationSummary { Id = "conv-1", RecordedToolsFingerprint = "tools-1" };

        ConversationSummarizer.Fold(summary, Request("""{"newMessageCount":1,"lastMessageFingerprint":"two"}"""));

        Assert.Equal("tools-1", summary.RecordedToolsFingerprint);
    }

    [Fact]
    public void Fold_NullFingerprints_LeaveTheStoredStateUntouched()
    {
        var summary = new ConversationSummary
        {
            Id = "conv-1",
            RecordedMessageCount = 4,
            LastRecordedMessageFingerprint = "last",
            RecordedToolsFingerprint = "tools-1",
        };

        ConversationSummarizer.Fold(
            summary,
            Request("""{"newMessageCount":0,"lastMessageFingerprint":null,"toolsFingerprint":null}"""));

        Assert.Equal(4, summary.RecordedMessageCount);
        Assert.Equal("last", summary.LastRecordedMessageFingerprint);
        Assert.Equal("tools-1", summary.RecordedToolsFingerprint);
    }

    [Fact]
    public void Fold_LegacyRequestEventWithoutDedupeFields_LeavesTheStateEmpty()
    {
        var summary = new ConversationSummary { Id = "conv-1" };

        ConversationSummarizer.Fold(summary, Request("""{"body":{"messages":[{"role":"user","content":"hi"}]},"messageCount":1}"""));

        Assert.Equal(0, summary.RecordedMessageCount);
        Assert.Null(summary.LastRecordedMessageFingerprint);
        Assert.Null(summary.RecordedToolsFingerprint);
    }

    [Fact]
    public void Fold_EventWithoutData_LeavesTheStateUntouched()
    {
        var summary = new ConversationSummary { Id = "conv-1", RecordedMessageCount = 3 };

        ConversationSummarizer.Fold(summary, new ConversationEvent(
            ConversationEventKind.Request, _start, "conv-1", "req-1", "provider-a", "gpt-test", "/v1/chat/completions", null));

        Assert.Equal(3, summary.RecordedMessageCount);
    }

    [Fact]
    public void Fold_NonRequestEvent_LeavesTheRecordedStateUntouched()
    {
        var summary = new ConversationSummary { Id = "conv-1", RecordedMessageCount = 4, LastRecordedMessageFingerprint = "last" };

        ConversationSummarizer.Fold(
            summary,
            new ConversationEvent(
                ConversationEventKind.Response, _start, "conv-1", "req-1", "provider-a", "gpt-test", "/v1/chat/completions",
                Json("""{"newMessageCount":9,"lastMessageFingerprint":"other"}""")));

        Assert.Equal(4, summary.RecordedMessageCount);
        Assert.Equal("last", summary.LastRecordedMessageFingerprint);
    }

    [Fact]
    public void Summarize_OverTurnsThatOmitTheirTools_EndsOnTheLastKnownState()
    {
        var events = new[]
        {
            Request("""{"newMessageCount":1,"lastMessageFingerprint":"one","toolsFingerprint":"tools-1"}"""),
            Request("""{"newMessageCount":2,"lastMessageFingerprint":"two"}"""),
            Request("""{"newMessageCount":1,"lastMessageFingerprint":"three","toolsFingerprint":"tools-2"}"""),
        };

        var summary = ConversationSummarizer.Summarize("conv-1", events);

        Assert.Equal(4, summary.RecordedMessageCount);
        Assert.Equal("three", summary.LastRecordedMessageFingerprint);
        Assert.Equal("tools-2", summary.RecordedToolsFingerprint);
    }

    private static ConversationEvent Request(string dataJson) =>
        new(ConversationEventKind.Request, _start, "conv-1", "req-1", "provider-a", "gpt-test", "/v1/chat/completions", Json(dataJson));

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
