namespace LlmProxy.Api.Middleware;

using System.Net;
using System.Net.Sockets;
using LlmProxy.Application.Configuration;
using LlmProxy.Application.Logging;

/// <summary>
/// Keeps the operator surface on the machine the proxy runs on, and passes every other request through.
/// </summary>
/// <remarks>
/// The operator surface is the admin API, the provider API and the static operator pages. Since the conversation
/// audit log was added it carries the full text of every user prompt, reasoning block and tool result, and the
/// provider API reports provider configuration, so a remote client must not be able to read either.
/// The decision is made on the TCP peer address alone. Forwarded-for style headers are deliberately not consulted:
/// a client sets them to whatever it likes, and trusting them would make the restriction decorative.
/// </remarks>
public sealed class AdminAccessMiddleware
{
    /// <summary>Path prefixes that make up the operator surface.</summary>
    private static readonly string[] _adminPrefixes =
    [
        "/admin",
        "/api/providers",
        "/admin.html",
        "/conversations.html",
        "/conversation.html",
    ];

    private readonly RequestDelegate _next;
    private readonly AdminAccessOptions _options;
    private readonly ILogger<AdminAccessMiddleware> _logger;

    public AdminAccessMiddleware(
        RequestDelegate next,
        AdminAccessOptions options,
        ILogger<AdminAccessMiddleware> logger)
    {
        _next = next;
        _options = options;
        _logger = logger;
    }

    /// <summary>Refuses an operator path that did not arrive over the loopback interface.</summary>
    /// <param name="context">Current request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.RequireLocalhost || !IsAdminPath(context.Request.Path) || IsLoopback(context.Connection.RemoteIpAddress))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        _logger.AdminSurfaceBlocked(context.Request.Path.Value ?? string.Empty);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            """{"error":{"message":"The admin surface is restricted to the machine the proxy runs on","type":"forbidden_error","param":null,"code":"403"}}""",
            context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>Whether a request path belongs to the operator surface.</summary>
    /// <param name="path">Request path to classify.</param>
    /// <remarks>
    /// Whole-segment matching, so a client path that merely starts with the same letters — /administrators — is
    /// not swept into the restricted set. The operator pages are listed explicitly because /admin.html is one
    /// segment and so does not fall under /admin.
    /// </remarks>
    internal static bool IsAdminPath(PathString path) =>
        _adminPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a peer address is the loopback interface, over IPv4 or IPv6.</summary>
    /// <param name="address">Peer address of the connection; null when the transport has none.</param>
    internal static bool IsLoopback(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        // Kestrel on a dual-stack socket reports an IPv4 peer as an IPv4-mapped IPv6 address.
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return IPAddress.IsLoopback(address);
    }
}
