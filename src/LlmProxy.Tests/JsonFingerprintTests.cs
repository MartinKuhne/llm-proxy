namespace LlmProxy.Tests;

using System.Text.Json;
using LlmProxy.Application.Services;

public class JsonFingerprintTests
{
    [Fact]
    public void Of_SameContentWrittenInADifferentKeyOrder_FingerprintsTheSame()
    {
        Assert.Equal(Of("""{"b":1,"a":[{"z":2,"y":3}]}"""), Of("""{"a":[{"y":3,"z":2}],"b":1}"""));
    }

    [Fact]
    public void Of_KeepsArrayOrderMeaningful()
    {
        Assert.NotEqual(Of("""["a","b"]"""), Of("""["b","a"]"""));
    }

    [Fact]
    public void Of_DifferentContent_FingerprintsDifferently()
    {
        Assert.NotEqual(Of("""{"a":"one"}"""), Of("""{"a":"two"}"""));
    }

    [Fact]
    public void Of_IsSixteenLowercaseHexCharacters()
    {
        Assert.Matches("^[0-9a-f]{16}$", Of("""{"a":[1,{"b":"x"}]}""")!);
    }

    [Fact]
    public void Of_NullValue_HasNoFingerprint()
    {
        Assert.Null(Of("null"));
    }

    [Fact]
    public void Of_UndefinedElement_HasNoFingerprint()
    {
        Assert.Null(JsonFingerprint.Of(default));
    }

    [Fact]
    public void Of_ScalarsAndEmptyContainers_AreFingerprinted()
    {
        Assert.NotNull(Of("\"text\""));
        Assert.NotNull(Of("42"));
        Assert.NotNull(Of("{}"));
        Assert.NotNull(Of("[]"));
    }

    private static string? Of(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonFingerprint.Of(document.RootElement);
    }
}
