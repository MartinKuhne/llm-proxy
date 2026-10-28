namespace LlmProxy.Api.Responses;

/// <summary>
/// Error envelope shared with the OpenAI-compatible error responses of this service.
/// </summary>
public class ErrorResponseDto
{
    public ErrorDetailDto Error { get; set; } = new();
}
