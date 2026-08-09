using LlmProxy.Application.Services;
using LlmProxy.Domain;

namespace LlmProxy.Tests;

public class DefaultErrorParserTests
{
    private readonly DefaultErrorParser _parser = new();

    [Fact]
    public void Parse_NullResponseBody_ReturnsStatusBasedError()
    {
        var result = _parser.Parse(429, null);

        Assert.Equal(429, result.Code);
        Assert.Equal(ProviderErrorType.RateLimitExceeded, result.ErrorType);
        Assert.Equal("Rate limit exceeded", result.Message);
    }

    [Fact]
    public void Parse_500_ReturnsServerError()
    {
        var result = _parser.Parse(500, null);

        Assert.Equal(500, result.Code);
        Assert.Equal(ProviderErrorType.Server, result.ErrorType);
        Assert.Equal("Internal server error", result.Message);
    }

    [Fact]
    public void Parse_WithJsonBody_ExtractsErrorMessage()
    {
        var body = """{"error": {"message": "custom error"}}""";

        var result = _parser.Parse(400, body);

        Assert.Equal("custom error", result.Message);
        Assert.Equal(ProviderErrorType.InvalidRequest, result.ErrorType);
    }

    [Fact]
    public void Parse_WithStringError_ExtractsMessage()
    {
        var body = """{"error": "something went wrong"}""";

        var result = _parser.Parse(502, body);

        Assert.Equal("something went wrong", result.Message);
        Assert.Equal(ProviderErrorType.ProviderUnavailable, result.ErrorType);
    }

    [Fact]
    public void Parse_InvalidJson_FallsBackToStatusBasedError()
    {
        var result = _parser.Parse(404, "not json at all");

        Assert.Equal(404, result.Code);
        Assert.Equal(ProviderErrorType.NotFound, result.ErrorType);
        Assert.Equal("Not found", result.Message);
    }

    [Theory]
    [InlineData(400, ProviderErrorType.InvalidRequest)]
    [InlineData(401, ProviderErrorType.Authentication)]
    [InlineData(402, ProviderErrorType.PaymentRequired)]
    [InlineData(403, ProviderErrorType.PermissionDenied)]
    [InlineData(404, ProviderErrorType.NotFound)]
    [InlineData(408, ProviderErrorType.Timeout)]
    [InlineData(412, ProviderErrorType.PreconditionFailed)]
    [InlineData(413, ProviderErrorType.PayloadTooLarge)]
    [InlineData(422, ProviderErrorType.Unprocessable)]
    [InlineData(429, ProviderErrorType.RateLimitExceeded)]
    [InlineData(500, ProviderErrorType.Server)]
    [InlineData(502, ProviderErrorType.ProviderUnavailable)]
    [InlineData(503, ProviderErrorType.ProviderOverloaded)]
    [InlineData(504, ProviderErrorType.Timeout)]
    public void Parse_MapsAllStatusCodesCorrectly(int statusCode, ProviderErrorType expected)
    {
        var result = _parser.Parse(statusCode, null);

        Assert.Equal(expected, result.ErrorType);
    }
}
