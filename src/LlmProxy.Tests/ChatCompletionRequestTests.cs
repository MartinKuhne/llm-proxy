namespace LlmProxy.Tests;

using LlmProxy.Domain;

public class ChatCompletionRequestTests
{
    [Fact]
    public void ChatCompletionRequest_CanBeCreated()
    {
        var messages = new[] { new ChatMessage("user", "Hello") };
        var request = new ChatCompletionRequest("model1", messages, true);

        Assert.Equal("model1", request.Model);
        Assert.Single(request.Messages);
        Assert.True(request.Stream);
    }

    [Fact]
    public void ChatCompletionRequest_DefaultStreamIsFalse()
    {
        var messages = new[] { new ChatMessage("user", "Hello") };
        var request = new ChatCompletionRequest("model1", messages);

        Assert.False(request.Stream);
    }

    [Fact]
    public void ChatCompletionRequest_IsImmutable()
    {
        var messages = new[] { new ChatMessage("user", "Hello") };
        var request1 = new ChatCompletionRequest("model1", messages, false);
        var request2 = request1 with { Stream = true };

        Assert.False(request1.Stream);
        Assert.True(request2.Stream);
    }
}
