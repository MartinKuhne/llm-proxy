namespace LlmProxy.Tests;

using System.Text.Json;
using LlmProxy.Application.Services;
using Microsoft.AspNetCore.Http;

public class ConversationIdResolverTests
{
    private readonly ConversationIdResolver _resolver = new();

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
