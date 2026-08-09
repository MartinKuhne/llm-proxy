namespace LlmProxy.Application.Services;

using LlmProxy.Domain;

public interface IProviderErrorParser
{
    ProviderError Parse(int statusCode, string? responseBody);
}
