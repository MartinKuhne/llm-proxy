namespace LlmProxy.Tests;

using LlmProxy.Domain;

public class TokenCountLogTests
{
    [Fact]
    public void TokenCountLog_CanBeCreated()
    {
        var date = new DateTime(2024, 1, 1, 12, 0, 0);
        var log = new TokenCountLog(
            Date: date,
            Provider: "provider1",
            Model: "model1",
            PromptTokens: 10,
            CompletionTokens: 20,
            TotalTokens: 30,
            CachedTokens: 5,
            RequestCount: 1,
            ElapsedMs: 1000);

        Assert.Equal(date, log.Date);
        Assert.Equal("provider1", log.Provider);
        Assert.Equal("model1", log.Model);
        Assert.Equal(10, log.PromptTokens);
        Assert.Equal(20, log.CompletionTokens);
        Assert.Equal(30, log.TotalTokens);
        Assert.Equal(5, log.CachedTokens);
        Assert.Equal(1, log.RequestCount);
        Assert.Equal(1000, log.ElapsedMs);
    }

    [Fact]
    public void TokenCountLog_CanBeCreatedWithDefaultCachedTokens()
    {
        var date = new DateTime(2024, 1, 1, 12, 0, 0);
        var log = new TokenCountLog(
            Date: date,
            Provider: "provider1",
            Model: "model1",
            PromptTokens: 10,
            CompletionTokens: 20,
            TotalTokens: 30,
            CachedTokens: 0,
            RequestCount: 1,
            ElapsedMs: 1000);

        Assert.Equal(0, log.CachedTokens);
    }

    [Fact]
    public void TokenCountLog_IsImmutable()
    {
        var log1 = new TokenCountLog(
            Date: DateTime.UtcNow,
            Provider: "provider1",
            Model: "model1",
            PromptTokens: 10,
            CompletionTokens: 20,
            TotalTokens: 30,
            CachedTokens: 5,
            RequestCount: 1,
            ElapsedMs: 1000);

        var log2 = log1 with { RequestCount = 2, TotalTokens = 35 };

        Assert.Equal(1, log1.RequestCount);
        Assert.Equal(30, log1.TotalTokens);
        Assert.Equal(5, log1.CachedTokens);
        Assert.Equal(2, log2.RequestCount);
        Assert.Equal(35, log2.TotalTokens);
        Assert.Equal(5, log2.CachedTokens); // CachedTokens should remain unchanged
    }
}
