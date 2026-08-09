namespace LlmProxy.Tests;

using LlmProxy.Application.Services;

public class ProviderUsageStatTests
{
    [Fact]
    public void ProviderUsageStat_HasDefaultValues()
    {
        var stat = new ProviderUsageStat();

        Assert.Equal(string.Empty, stat.Provider);
        Assert.Equal(string.Empty, stat.Model);
        Assert.Equal(0, stat.RequestCount);
        Assert.Equal(0, stat.PromptTokens);
        Assert.Equal(0, stat.CompletionTokens);
        Assert.Equal(0, stat.TotalTokens);
        Assert.Equal(0, stat.TotalElapsedMs);
    }

    [Fact]
    public void ProviderUsageStat_CanSetProperties()
    {
        var stat = new ProviderUsageStat
        {
            Provider = "provider1",
            Model = "model1",
            RequestCount = 5,
            PromptTokens = 100,
            CompletionTokens = 200,
            TotalTokens = 300,
            TotalElapsedMs = 5000
        };

        Assert.Equal("provider1", stat.Provider);
        Assert.Equal("model1", stat.Model);
        Assert.Equal(5, stat.RequestCount);
        Assert.Equal(100, stat.PromptTokens);
        Assert.Equal(200, stat.CompletionTokens);
        Assert.Equal(300, stat.TotalTokens);
        Assert.Equal(5000, stat.TotalElapsedMs);
    }
}
