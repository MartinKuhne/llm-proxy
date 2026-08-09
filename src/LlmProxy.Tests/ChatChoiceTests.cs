namespace LlmProxy.Tests;

using LlmProxy.Domain;

public class ChatChoiceTests
{
    [Fact]
    public void ChatChoice_CanBeCreated()
    {
        var message = new ChatMessage("assistant", "Response");
        var choice = new ChatChoice(0, message, "stop");

        Assert.Equal(0, choice.Index);
        Assert.Equal(message, choice.Message);
        Assert.Equal("stop", choice.FinishReason);
    }

    [Fact]
    public void ChatChoice_IsImmutable()
    {
        var message = new ChatMessage("assistant", "Response");
        var choice1 = new ChatChoice(0, message, "stop");
        var choice2 = choice1 with { Index = 1 };

        Assert.Equal(0, choice1.Index);
        Assert.Equal(1, choice2.Index);
    }
}
