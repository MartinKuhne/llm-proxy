namespace LlmProxy.Tests;

using LlmProxy.Domain;

public class UsageTests
{
    [Fact]
    public void Usage_CanBeCreated()
    {
        var usage = new Usage(10, 20, 30);

        Assert.Equal(10, usage.PromptTokens);
        Assert.Equal(20, usage.CompletionTokens);
        Assert.Equal(30, usage.TotalTokens);
        Assert.Equal(0, usage.CachedTokens); // Default value
    }

    [Fact]
    public void Usage_CanBeCreatedWithCachedTokens()
    {
        var usage = new Usage(10, 20, 30, 5);

        Assert.Equal(10, usage.PromptTokens);
        Assert.Equal(20, usage.CompletionTokens);
        Assert.Equal(30, usage.TotalTokens);
        Assert.Equal(5, usage.CachedTokens);
    }

    [Fact]
    public void Usage_IsImmutable()
    {
        var usage1 = new Usage(10, 20, 30);
        var usage2 = usage1 with { TotalTokens = 35 };

        Assert.Equal(30, usage1.TotalTokens);
        Assert.Equal(35, usage2.TotalTokens);
    }
}
