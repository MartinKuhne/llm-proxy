using System.Text.Json;
using LlmProxy.Application.Services;
using LlmProxy.Domain;

namespace LlmProxy.Tests;

public class OpenRouterErrorParserTests
{
    private readonly OpenRouterErrorParser _parser = new();

    [Fact]
    public void Parse_NullResponse_DelegatesToFallback()
    {
        var result = _parser.Parse(500, null);

        Assert.Equal(500, result.Code);
        Assert.Equal("Internal server error", result.Message);
        Assert.Equal(ProviderErrorType.Server, result.ErrorType);
        Assert.Null(result.ProviderCode);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public void Parse_NonOpenRouterJson_DelegatesToFallback()
    {
        var body = """{"error": {"message": "something went wrong"}}""";

        var result = _parser.Parse(400, body);

        Assert.Equal(400, result.Code);
        Assert.Equal("something went wrong", result.Message);
        Assert.Equal(ProviderErrorType.InvalidRequest, result.ErrorType);
        Assert.Null(result.ProviderCode);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public void Parse_InvalidJson_DelegatesToFallback()
    {
        var result = _parser.Parse(400, "not json at all");

        Assert.Equal(400, result.Code);
        Assert.Equal("Bad request", result.Message);
        Assert.Equal(ProviderErrorType.InvalidRequest, result.ErrorType);
    }

    [Fact]
    public void Parse_OpenRouterError_WithFullMetadata()
    {
        var body = """
        {
          "error": {
            "code": 429,
            "message": "Rate limit exceeded",
            "metadata": {
              "error_type": "rate_limit_exceeded",
              "provider_code": "rate_limited"
            }
          }
        }
        """;

        var result = _parser.Parse(429, body);

        Assert.Equal(429, result.Code);
        Assert.Equal("Rate limit exceeded", result.Message);
        Assert.Equal(ProviderErrorType.RateLimitExceeded, result.ErrorType);
        Assert.Equal("rate_limited", result.ProviderCode);
        Assert.NotNull(result.Metadata);
        Assert.True(result.Metadata!.ContainsKey("error_type"));
        Assert.True(result.Metadata.ContainsKey("provider_code"));
    }

    [Fact]
    public void Parse_OpenRouterError_WithoutMetadata_FallsBackToHttpStatus()
    {
        var body = """
        {
          "error": {
            "code": 400,
            "message": "Invalid request format"
          }
        }
        """;

        var result = _parser.Parse(400, body);

        Assert.Equal(400, result.Code);
        Assert.Equal("Invalid request format", result.Message);
        Assert.Equal(ProviderErrorType.InvalidRequest, result.ErrorType);
        Assert.Null(result.ProviderCode);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public void Parse_OpenRouterError_MetadataOverridesHttpStatus()
    {
        var body = """
        {
          "error": {
            "code": 429,
            "message": "Too many requests",
            "metadata": {
              "error_type": "context_length_exceeded"
            }
          }
        }
        """;

        var result = _parser.Parse(429, body);

        Assert.Equal(ProviderErrorType.ContextLengthExceeded, result.ErrorType);
    }

    [Fact]
    public void Parse_500Error_WithServerErrorType()
    {
        var body = """
        {
          "error": {
            "code": 500,
            "message": "An error occurred",
            "metadata": {
              "error_type": "server"
            }
          }
        }
        """;

        var result = _parser.Parse(500, body);

        Assert.Equal(500, result.Code);
        Assert.Equal(ProviderErrorType.Server, result.ErrorType);
    }

    [Fact]
    public void Parse_500Error_WithoutMetadata_MapsToServer()
    {
        var body = """
        {
          "error": {
            "message": "An error occurred"
          }
        }
        """;

        var result = _parser.Parse(500, body);

        Assert.Equal(ProviderErrorType.Server, result.ErrorType);
        Assert.Equal("An error occurred", result.Message);
    }

    [Fact]
    public void Parse_MissingMessage_UsesDefaultMessage()
    {
        var body = """
        {
          "error": {
            "code": 401
          }
        }
        """;

        var result = _parser.Parse(401, body);

        Assert.Equal("Unauthorized", result.Message);
        Assert.Equal(ProviderErrorType.Authentication, result.ErrorType);
    }

    [Fact]
    public void Parse_MidStreamError_FormatsAreHandled()
    {
        var body = """
        {
          "choices": [
            {
              "finish_reason": "error",
              "delta": {}
            }
          ],
          "error": {
            "code": 502,
            "message": "Provider returned an error",
            "metadata": {
              "error_type": "provider_unavailable",
              "provider_code": "upstream_timeout"
            }
          }
        }
        """;

        var result = _parser.Parse(200, body);

        Assert.Equal(502, result.Code);
        Assert.Equal("Provider returned an error", result.Message);
        Assert.Equal(ProviderErrorType.ProviderUnavailable, result.ErrorType);
        Assert.Equal("upstream_timeout", result.ProviderCode);
    }

    [Fact]
    public void Parse_AllErrorTypes_MapCorrectly()
    {
        var expectedMappings = new Dictionary<string, ProviderErrorType>
        {
            ["context_length_exceeded"] = ProviderErrorType.ContextLengthExceeded,
            ["max_tokens_exceeded"] = ProviderErrorType.MaxTokensExceeded,
            ["token_limit_exceeded"] = ProviderErrorType.TokenLimitExceeded,
            ["string_too_long"] = ProviderErrorType.StringTooLong,
            ["authentication"] = ProviderErrorType.Authentication,
            ["permission_denied"] = ProviderErrorType.PermissionDenied,
            ["payment_required"] = ProviderErrorType.PaymentRequired,
            ["rate_limit_exceeded"] = ProviderErrorType.RateLimitExceeded,
            ["provider_overloaded"] = ProviderErrorType.ProviderOverloaded,
            ["provider_unavailable"] = ProviderErrorType.ProviderUnavailable,
            ["invalid_request"] = ProviderErrorType.InvalidRequest,
            ["invalid_prompt"] = ProviderErrorType.InvalidPrompt,
            ["not_found"] = ProviderErrorType.NotFound,
            ["precondition_failed"] = ProviderErrorType.PreconditionFailed,
            ["payload_too_large"] = ProviderErrorType.PayloadTooLarge,
            ["unprocessable"] = ProviderErrorType.Unprocessable,
            ["content_policy_violation"] = ProviderErrorType.ContentPolicyViolation,
            ["refusal"] = ProviderErrorType.Refusal,
            ["invalid_image"] = ProviderErrorType.InvalidImage,
            ["image_too_large"] = ProviderErrorType.ImageTooLarge,
            ["image_too_small"] = ProviderErrorType.ImageTooSmall,
            ["unsupported_image_format"] = ProviderErrorType.UnsupportedImageFormat,
            ["image_not_found"] = ProviderErrorType.ImageNotFound,
            ["image_download_failed"] = ProviderErrorType.ImageDownloadFailed,
            ["server"] = ProviderErrorType.Server,
            ["timeout"] = ProviderErrorType.Timeout,
            ["unmapped"] = ProviderErrorType.Unmapped
        };

        foreach (var (errorType, expected) in expectedMappings)
        {
            var body = $$"""
            {
              "error": {
                "message": "test",
                "metadata": {
                  "error_type": "{{errorType}}"
                }
              }
            }
            """;

            var result = _parser.Parse(200, body);

            Assert.Equal(expected, result.ErrorType);
        }
    }

    [Fact]
    public void Parse_ErrorTypeMap_IsCaseInsensitive()
    {
        var body = """
        {
          "error": {
            "message": "test",
            "metadata": {
              "error_type": "Rate_Limit_Exceeded"
            }
          }
        }
        """;

        var result = _parser.Parse(200, body);

        Assert.Equal(ProviderErrorType.RateLimitExceeded, result.ErrorType);
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
    [InlineData(418, ProviderErrorType.Unknown)]
    [InlineData(501, ProviderErrorType.Unknown)]
    public void Parse_HttpStatusMaps_Correctly(int statusCode, ProviderErrorType expected)
    {
        var body = """
        {
          "error": {
            "message": "test error"
          }
        }
        """;

        var result = _parser.Parse(statusCode, body);

        Assert.Equal(expected, result.ErrorType);
    }

    [Fact]
    public void Parse_ModerationError_WithRichMetadata()
    {
        var body = """
        {
          "error": {
            "code": 403,
            "message": "Content policy violation",
            "metadata": {
              "error_type": "content_policy_violation",
              "provider_code": "moderation_flagged",
              "reasons": ["violence", "self-harm"],
              "flagged_input": "some problematic text here",
              "provider_name": "openai",
              "model_slug": "gpt-4"
            }
          }
        }
        """;

        var result = _parser.Parse(403, body);

        Assert.Equal(403, result.Code);
        Assert.Equal("Content policy violation", result.Message);
        Assert.Equal(ProviderErrorType.ContentPolicyViolation, result.ErrorType);
        Assert.Equal("moderation_flagged", result.ProviderCode);
        Assert.NotNull(result.Metadata);
        Assert.True(result.Metadata!.ContainsKey("reasons"));
        Assert.True(result.Metadata.ContainsKey("flagged_input"));
        Assert.True(result.Metadata.ContainsKey("provider_name"));
        Assert.True(result.Metadata.ContainsKey("model_slug"));
    }

    [Fact]
    public void Parse_EmptyMetadataObject_TreatedAsNoMetadata()
    {
        var body = """
        {
          "error": {
            "code": 502,
            "message": "Provider unavailable",
            "metadata": {}
          }
        }
        """;

        var result = _parser.Parse(502, body);

        Assert.Equal(502, result.Code);
        Assert.Equal(ProviderErrorType.ProviderUnavailable, result.ErrorType);
        Assert.Null(result.ProviderCode);
    }

    [Fact]
    public void FromException_DelegatesToProviderError()
    {
        var ex = new OperationCanceledException("timed out");

        var result = ProviderError.FromException(ex);

        Assert.Equal(ProviderErrorType.Timeout, result.ErrorType);
        Assert.Equal("timed out", result.Message);
        Assert.Null(result.Code);
        Assert.Null(result.ProviderCode);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public void FromException_WithCustomMessage()
    {
        var ex = new InvalidOperationException("unexpected");

        var result = ProviderError.FromException(ex, "custom message");

        Assert.Equal(ProviderErrorType.Unknown, result.ErrorType);
        Assert.Equal("custom message", result.Message);
    }

    [Fact]
    public void Parse_CodePropertyInErrorOverridesStatusCode()
    {
        var body = """
        {
          "error": {
            "code": 402,
            "message": "Insufficient credits"
          }
        }
        """;

        var result = _parser.Parse(429, body);

        Assert.Equal(402, result.Code);
        Assert.Equal(ProviderErrorType.RateLimitExceeded, result.ErrorType);
    }

    [Fact]
    public void Parse_CodePropertyAsString_IgnoresStringCode()
    {
        var body = """
        {
          "error": {
            "code": "not_a_number",
            "message": "Error"
          }
        }
        """;

        var result = _parser.Parse(400, body);

        Assert.Equal(400, result.Code);
    }
}
