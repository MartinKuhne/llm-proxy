using LlmProxy.Application.Services;
using LlmProxy.Domain;

namespace LlmProxy.Tests;

public class HttpStatusMappingTests
{
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
    public void MapErrorType_MapsKnownStatusCodes(int statusCode, ProviderErrorType expected)
    {
        Assert.Equal(expected, HttpStatusMapping.MapErrorType(statusCode));
    }

    [Theory]
    [InlineData(418)]
    [InlineData(501)]
    [InlineData(999)]
    public void MapErrorType_ReturnsUnknownForUnmappedStatusCodes(int statusCode)
    {
        Assert.Equal(ProviderErrorType.Unknown, HttpStatusMapping.MapErrorType(statusCode));
    }

    [Theory]
    [InlineData(400, "Bad request")]
    [InlineData(401, "Unauthorized")]
    [InlineData(402, "Payment required")]
    [InlineData(403, "Forbidden")]
    [InlineData(404, "Not found")]
    [InlineData(408, "Request timeout")]
    [InlineData(412, "Precondition failed")]
    [InlineData(413, "Payload too large")]
    [InlineData(422, "Unprocessable entity")]
    [InlineData(429, "Rate limit exceeded")]
    [InlineData(500, "Internal server error")]
    [InlineData(502, "Bad gateway")]
    [InlineData(503, "Service unavailable")]
    [InlineData(504, "Gateway timeout")]
    public void GetDefaultMessage_ReturnsCorrectMessage(int statusCode, string expected)
    {
        Assert.Equal(expected, HttpStatusMapping.GetDefaultMessage(statusCode));
    }

    [Fact]
    public void GetDefaultMessage_ReturnsGenericMessageForUnmappedStatusCodes()
    {
        var result = HttpStatusMapping.GetDefaultMessage(418);

        Assert.Equal("Upstream returned HTTP 418", result);
    }

    [Fact]
    public void GetDefaultMessage_ConsistentWithMapErrorType()
    {
        // Every status code that maps to a known error type should have a meaningful message
        for (int code = 400; code <= 599; code++)
        {
            var errorType = HttpStatusMapping.MapErrorType(code);
            var message = HttpStatusMapping.GetDefaultMessage(code);

            if (errorType != ProviderErrorType.Unknown)
            {
                Assert.False(string.IsNullOrWhiteSpace(message),
                    $"HTTP {code} maps to {errorType} but has no default message");
            }
        }
    }
}
