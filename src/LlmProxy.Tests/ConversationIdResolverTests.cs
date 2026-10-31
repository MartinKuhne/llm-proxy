namespace LlmProxy.Tests;

using System.Text.Json;
using LlmProxy.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

public class ConversationIdResolverTests
{
    private readonly ConversationIdResolver _resolver = new(Substitute.For<ILogger<ConversationIdResolver>>());

    [Theory]
    [InlineData("chat-42")]
    [InlineData("a")]
    [InlineData("A_b-9.d")]
    [InlineData("0123456789")]
    public void Resolve_ConformingHeader_IsUsedAsGiven(string value)
    {
        var body = Json("""{"messages":[{"role":"user","content":"hi"}]}""");

        Assert.Equal(value, _resolver.Resolve(Request(value), body));
    }

    /// <summary>
    /// A header the client controls is not a trusted identity. Anything outside the strict shape is ignored, so it
    /// cannot name a conversation it does not own and cannot be folded onto a file another client holds.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\evil")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a b")]
    [InlineData("a<b>|")]
    [InlineData("a\tb")]
    [InlineData("a\u0000b")]
    [InlineData("a\nb")]
    [InlineData("ünïcode")]
    public void Resolve_NonConformingHeader_IsIgnoredAndDerivesItsOwnConversation(string value)
    {
        var body = Json("""{"messages":[{"role":"user","content":"hi"}]}""");

        var id = _resolver.Resolve(Request(value), body);

        Assert.Matches("^[0-9a-f]{16}$", id);
        Assert.Equal(ConversationIdResolver.DeriveFromMessages(body), id);
    }

    [Fact]
    public void Resolve_HeaderAtTheLengthLimit_IsUsed_AboveItIsIgnored()
    {
        var body = Json("""{"messages":[{"role":"user","content":"hi"}]}""");

        Assert.Equal(new string('k', 100), _resolver.Resolve(Request(new string('k', 100)), body));
        Assert.Matches("^[0-9a-f]{16}$", _resolver.Resolve(Request(new string('k', 101)), body));
    }

    [Fact]
    public void Resolve_IdsThatWouldCollideOnOneFile_DoNotShareAConversation()
    {
        var body = Json("""{"messages":[{"role":"user","content":"hi"}]}""");

        // "a/b" and "a_b" sanitize to the same file name, so honouring the former would let one client write into
        // the other's conversation. The non-conforming value is dropped instead.
        var traversalish = _resolver.Resolve(Request("a/b"), body);
        var underscore = _resolver.Resolve(Request("a_b"), body);

        Assert.Equal("a_b", underscore);
        Assert.Matches("^[0-9a-f]{16}$", traversalish);
        Assert.NotEqual(traversalish, underscore);
    }

    [Fact]
    public void Resolve_UsesHeaderAndTrimsIt()
    {
        var body = Json("""{"messages":[{"role":"user","content":"hi"}]}""");

        var id = _resolver.Resolve(Request("  chat-42  "), body);

        Assert.Equal("chat-42", id);
    }

    [Fact]
    public void Resolve_BlankHeaderFallsBackToDerivedId()
    {
        var body = Json("""{"messages":[{"role":"user","content":"hi"}]}""");

        var id = _resolver.Resolve(Request("   "), body);

        Assert.Equal(16, id.Length);
        Assert.Matches("^[0-9a-f]{16}$", id);
    }

    [Fact]
    public void Resolve_SameOpeningWithGrowingHistory_IsStable()
    {
        var first = Json("""{"messages":[{"role":"system","content":"be brief"},{"role":"user","content":"hello"}]}""");
        var replay = Json(
            """
            {"messages":[
              {"role":"system","content":"be brief"},
              {"role":"user","content":"hello"},
              {"role":"assistant","content":"hi"},
              {"role":"user","content":"and again"}
            ]}
            """);

        Assert.Equal(_resolver.Resolve(Request(), first), _resolver.Resolve(Request(), replay));
    }

    [Fact]
    public void Resolve_DifferentFirstUserMessage_ProducesDifferentId()
    {
        var first = Json("""{"messages":[{"role":"user","content":"hello"}]}""");
        var second = Json("""{"messages":[{"role":"user","content":"goodbye"}]}""");

        Assert.NotEqual(_resolver.Resolve(Request(), first), _resolver.Resolve(Request(), second));
    }

    [Fact]
    public void DeriveFromMessages_ArrayContentPartsMatchEquivalentStringContent()
    {
        var parts = Json(
            """
            {"messages":[
              {"role":"system","content":[{"type":"text","text":"be brief"}]},
              {"role":"user","content":[{"type":"text","text":"hel"},{"type":"text","text":"lo"}]}
            ]}
            """);
        var plain = Json("""{"messages":[{"role":"system","content":"be brief"},{"role":"user","content":"hello"}]}""");

        Assert.Equal(ConversationIdResolver.DeriveFromMessages(plain), ConversationIdResolver.DeriveFromMessages(parts));
    }

    [Fact]
    public void DeriveFromMessages_SystemOnlyIsStableAndDiffersFromUserOnly()
    {
        var systemOnly = Json("""{"messages":[{"role":"system","content":"be brief"}]}""");
        var userOnly = Json("""{"messages":[{"role":"user","content":"be brief"}]}""");

        Assert.Equal(ConversationIdResolver.DeriveFromMessages(systemOnly), ConversationIdResolver.DeriveFromMessages(systemOnly));
        Assert.NotEqual(ConversationIdResolver.DeriveFromMessages(systemOnly), ConversationIdResolver.DeriveFromMessages(userOnly));
    }

    [Fact]
    public void DeriveFromMessages_WithoutOpeningMessagesReturnsUniqueNonEmptyIds()
    {
        var first = ConversationIdResolver.DeriveFromMessages(Json("""{"model":"gpt-test"}"""));
        var second = ConversationIdResolver.DeriveFromMessages(Json("""{"model":"gpt-test"}"""));

        Assert.NotEmpty(first);
        Assert.NotEmpty(second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DeriveFromMessages_NullBodyReturnsFreshId()
    {
        Assert.False(string.IsNullOrWhiteSpace(ConversationIdResolver.DeriveFromMessages(null)));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"just a string\"")]
    [InlineData("42")]
    [InlineData("""{"messages":"not-an-array"}""")]
    [InlineData("""{"messages":["a string",42,null]}""")]
    [InlineData("""{"messages":[{"role":"user"}]}""")]
    [InlineData("""{"messages":[{"role":"user","content":null}]}""")]
    [InlineData("""{"messages":[{"role":"user","content":[{"type":"image_url"}]}]}""")]
    [InlineData("""{"messages":[{"role":"user","content":"a"},{"role":"user"}]}""")]
    public void DeriveFromMessages_OddShapesNeverThrow(string bodyJson)
    {
        var id = ConversationIdResolver.DeriveFromMessages(Json(bodyJson));

        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    private static HttpRequest Request(string? headerValue = null)
    {
        var context = new DefaultHttpContext();
        if (headerValue is not null)
        {
            context.Request.Headers[ConversationIdResolver.HeaderName] = headerValue;
        }

        return context.Request;
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
