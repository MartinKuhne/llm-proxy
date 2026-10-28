namespace LlmProxy.Api.Responses;

/// <summary>
/// Details of an <see cref="ErrorResponseDto"/>.
/// </summary>
public class ErrorDetailDto
{
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Param { get; set; }
    public string Code { get; set; } = string.Empty;
}
