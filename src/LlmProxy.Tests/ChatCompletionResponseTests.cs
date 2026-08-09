namespace LlmProxy.Tests;

using LlmProxy.Domain;

public class ChatCompletionResponseTests
{
    [Fact]
    public void ChatCompletionResponse_CanBeCreated()
    {
        var choices = new[] { new ChatChoice(0, new ChatMessage("assistant", "Response"), "stop") };
        var usage = new Usage(10, 20, 30);
        var response = new ChatCompletionResponse(
            Id: "chatcmpl-123",
            Object: "chat.completion",
            Created: 1234567890,
            Model: "model1",
            Choices: choices,
            Usage: usage);

        Assert.Equal("chatcmpl-123", response.Id);
        Assert.Equal("chat.completion", response.Object);
        Assert.Equal(1234567890, response.Created);
        Assert.Equal("model1", response.Model);
        Assert.Single(response.Choices);
        Assert.Equal(usage, response.Usage);
    }

    [Fact]
    public void ChatCompletionResponse_IsImmutable()
    {
        var choices = new[] { new ChatChoice(0, new ChatMessage("assistant", "Response"), "stop") };
        var usage = new Usage(10, 20, 30);
        var response1 = new ChatCompletionResponse(
            Id: "chatcmpl-123",
            Object: "chat.completion",
            Created: 1234567890,
            Model: "model1",
            Choices: choices,
            Usage: usage);

        var response2 = response1 with { Id = "chatcmpl-456" };

        Assert.Equal("chatcmpl-123", response1.Id);
        Assert.Equal("chatcmpl-456", response2.Id);
    }
}
