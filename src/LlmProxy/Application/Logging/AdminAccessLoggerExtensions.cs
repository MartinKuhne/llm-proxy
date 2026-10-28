namespace LlmProxy.Application.Logging;

using Microsoft.Extensions.Logging;

/// <summary>
/// Semantic logging events for the operator surface access control (event ids 200+).
/// </summary>
public static class AdminAccessLoggerExtensions
{
    private static readonly Action<ILogger, string, Exception?> _adminSurfaceBlocked =
        LoggerMessage.Define<string>(LogLevel.Warning, 200, "Refused remote access to the admin surface at {Path}");

    public static void AdminSurfaceBlocked(this ILogger logger, string path)
        => _adminSurfaceBlocked(logger, path, null);
}
