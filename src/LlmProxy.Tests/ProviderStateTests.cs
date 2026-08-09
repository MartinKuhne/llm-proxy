namespace LlmProxy.Tests;

using LlmProxy.Domain;

public class ProviderStateTests
{
    [Fact]
    public void ProviderState_CanBeCreated()
    {
        var lastUsed = new DateTime(2024, 1, 1, 12, 0, 0);
        var lastError = new DateTime(2024, 1, 1, 12, 1, 0);
        var ex = new Exception("Test");
        var state = new ProviderState(
            LastUsed: lastUsed,
            LastError: lastError,
            ErrorCount: 5,
            LastErrorMessage: "Test error",
            LastException: ex,
            LastErrorStatusCode: 500,
            LastErrorBody: "Error body",
            LastErrorType: ProviderErrorType.Server,
            LastProviderCode: "internal_error");

        Assert.Equal(lastUsed, state.LastUsed);
        Assert.Equal(lastError, state.LastError);
        Assert.Equal(5, state.ErrorCount);
        Assert.Equal("Test error", state.LastErrorMessage);
        Assert.Equal(ex, state.LastException);
        Assert.Equal(500, state.LastErrorStatusCode);
        Assert.Equal("Error body", state.LastErrorBody);
        Assert.Equal(ProviderErrorType.Server, state.LastErrorType);
        Assert.Equal("internal_error", state.LastProviderCode);
    }

    [Fact]
    public void ProviderState_WithNullValues()
    {
        var state = new ProviderState(
            LastUsed: DateTime.MinValue,
            LastError: null,
            ErrorCount: 0,
            LastErrorMessage: null,
            LastException: null,
            LastErrorStatusCode: null,
            LastErrorBody: null,
            LastErrorType: null,
            LastProviderCode: null);

        Assert.Equal(DateTime.MinValue, state.LastUsed);
        Assert.Null(state.LastError);
        Assert.Equal(0, state.ErrorCount);
        Assert.Null(state.LastErrorMessage);
        Assert.Null(state.LastException);
        Assert.Null(state.LastErrorStatusCode);
        Assert.Null(state.LastErrorBody);
        Assert.Null(state.LastErrorType);
        Assert.Null(state.LastProviderCode);
    }

    [Fact]
    public void ProviderState_IsImmutable()
    {
        var state1 = new ProviderState(
            LastUsed: DateTime.MinValue,
            LastError: null,
            ErrorCount: 0,
            LastErrorMessage: null,
            LastException: null,
            LastErrorStatusCode: null,
            LastErrorBody: null,
            LastErrorType: null,
            LastProviderCode: null);

        var state2 = state1 with { ErrorCount = 1, LastError = DateTime.UtcNow, LastErrorStatusCode = 429 };

        Assert.Equal(0, state1.ErrorCount);
        Assert.Equal(1, state2.ErrorCount);
        Assert.Null(state1.LastError);
        Assert.NotNull(state2.LastError);
        Assert.Null(state1.LastErrorStatusCode);
        Assert.Equal(429, state2.LastErrorStatusCode);
    }
}
