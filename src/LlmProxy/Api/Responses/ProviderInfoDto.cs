namespace LlmProxy.Api.Responses;

  public class ProviderInfoDto
{
    public string Key { get; set; } = string.Empty;
    public string FullModelKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool IsForced { get; set; }
    public bool IsAuto { get; set; }
    public int ErrorCount { get; set; }
    public DateTime? LastError { get; set; }
    public string? LastErrorMessage { get; set; }
    public int? LastErrorStatusCode { get; set; }
    public string? LastErrorBody { get; set; }
    public int Cost { get; set; }
    public int Complexity { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, CapabilityInfoDto> Capabilities { get; set; } = new();
}

public record CapabilityInfoDto(
    string Name,
    int Cost,
    int Complexity,
    bool Supported,
    int? MaxTokens = null,
    int? MaxContexts = null,
    Dictionary<string, object>? Limits = null,
    Dictionary<string, object>? Features = null,
    bool IsSelected = false);
