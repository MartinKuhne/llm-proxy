namespace LlmProxy.Application.Services;

using LlmProxy.Domain;

public interface ITokenCountLogger
{
    Task LogTokenCountAsync(TokenCountLog log, CancellationToken cancellationToken = default);
}
