namespace LlmProxy.Tests;

using LlmProxy.Application.Services;

/// <summary>
/// Covers <see cref="ConversationLocks"/> handing out one lock per conversation file: one lock for ids that map onto
/// the same file, a separate lock for every other conversation, so a busy conversation never serialises the rest of
/// the process.
/// </summary>
public class ConversationLocksTests
{
    [Fact]
    public void For_SameConversation_ReturnsTheSameLock()
    {
        var locks = new ConversationLocks();

        Assert.Same(locks.For("alpha"), locks.For("alpha"));
    }

    [Fact]
    public void For_IdsThatMapOntoOneFile_ReturnOneLock()
    {
        var locks = new ConversationLocks();

        // "a/b", "a\b" and "a_b" all sanitize to the file name "a_b", so they must not write it at the same time.
        Assert.Same(locks.For("a/b"), locks.For("a_b"));
        Assert.Same(locks.For("a\\b"), locks.For("a_b"));
    }

    [Fact]
    public void For_DifferentConversations_ReturnDifferentLocks()
    {
        var locks = new ConversationLocks();

        Assert.NotSame(locks.For("alpha"), locks.For("beta"));
    }

    [Fact]
    public async Task HoldingOneConversationLock_DoesNotMakeAnotherConversationWait()
    {
        var locks = new ConversationLocks();
        var alpha = locks.For("alpha");

        await alpha.WaitAsync();
        try
        {
            Assert.True(await locks.For("beta").WaitAsync(TimeSpan.FromSeconds(5)));
            locks.For("beta").Release();
        }
        finally
        {
            alpha.Release();
        }
    }
}
