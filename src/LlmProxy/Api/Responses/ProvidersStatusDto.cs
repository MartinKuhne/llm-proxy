namespace LlmProxy.Api.Responses;

public class ProvidersStatusDto
{
    public List<ProviderInfoDto> Providers { get; set; } = new();
    public bool IsForced { get; set; }
}
