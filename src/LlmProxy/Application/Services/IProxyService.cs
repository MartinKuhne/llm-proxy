namespace LlmProxy.Application.Services;

public interface IProxyService
{
    Task ProxyAsync(HttpContext context, string path, CancellationToken cancellationToken = default);
}
