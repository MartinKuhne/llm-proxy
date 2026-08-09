namespace LlmProxy.Tests;

using LlmProxy.Domain;

public class ChatMessageTests
{
    [Fact]
    public void ChatMessage_CanBeCreated()
    {
        var message = new ChatMessage("user", "Hello");

        Assert.Equal("user", message.Role);
        Assert.Equal("Hello", message.Content);
    }

    [Fact]
    public void ChatMessage_IsImmutable()
    {
        var msg1 = new ChatMessage("user", "Hello");
        var msg2 = msg1 with { Content = "World" };

        Assert.Equal("Hello", msg1.Content);
        Assert.Equal("World", msg2.Content);
    }
}
