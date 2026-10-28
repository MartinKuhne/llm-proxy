namespace LlmProxy.Application.Logging;

using Microsoft.Extensions.Logging;

public static class ProxyServiceLoggerExtensions
{
    private static readonly Action<ILogger, string, string, Exception?> _proxyError =
        LoggerMessage.Define<string, string>(LogLevel.Error, 10, "Proxy error for provider {BaseUrl}: {Message}");

    private static readonly Action<ILogger, string, int, Exception?> _proxyUpstreamError =
        LoggerMessage.Define<string, int>(LogLevel.Warning, 11, "Provider {BaseUrl} returned {StatusCode}");

    private static readonly Action<ILogger, Exception?> _proxyNoProviders =
        LoggerMessage.Define(LogLevel.Warning, 12, "No providers available for proxy request");

    private static readonly Action<ILogger, string, long, double, double, Exception?> _proxyUsage =
        LoggerMessage.Define<string, long, double, double>(
            LogLevel.Information, 13,
            "Provider {BaseUrl} usage: {ElapsedMs}ms, {PromptTokensPerSec:F1} prompt t/s, {CompletionTokensPerSec:F1} completion t/s");

    private static readonly Action<ILogger, string, Exception?> _proxyParsingUsage =
        LoggerMessage.Define<string>(LogLevel.Debug, 14, "Parsing usage from response for provider {BaseUrl}");

    private static readonly Action<ILogger, string, Exception?> _proxyNoUsageData =
        LoggerMessage.Define<string>(LogLevel.Warning, 15, "No usage data found in response from provider {BaseUrl}");

    private static readonly Action<ILogger, string, int, Exception?> _proxySsePacket =
        LoggerMessage.Define<string, int>(LogLevel.Debug, 16, "SSE packet from {BaseUrl}: {PayloadLength} bytes");

    private static readonly Action<ILogger, string, int, Exception?> _proxySseProgress =
        LoggerMessage.Define<string, int>(LogLevel.Information, 17, "SSE stream from {BaseUrl}: {PacketCount} packets received");

    private static readonly Action<ILogger, string, string, string, Exception?> _proxyUpstreamErrorWithBody =
        LoggerMessage.Define<string, string, string>(LogLevel.Warning, 18, "Provider {BaseUrl} returned {StatusCode}: {ResponseBody}");

    private static readonly Action<ILogger, string, int, Exception?> _proxyRetryAfter =
        LoggerMessage.Define<string, int>(LogLevel.Information, 19, "Provider {BaseUrl} requested {RetryAfterSeconds}s delay before retry");

    private static readonly Action<ILogger, string, Exception?> _proxyAuditBodyUnavailable =
        LoggerMessage.Define<string>(LogLevel.Debug, 20, "Could not capture the full request body for the audit log: {Reason}");

    private static readonly Action<ILogger, int, Exception?> _proxyAuditBodyTruncated =
        LoggerMessage.Define<int>(LogLevel.Debug, 21, "Audit copy of the request body truncated at {ByteLimit} bytes");

    public static void ProxyAuditBodyUnavailable(this ILogger logger, string reason, Exception? exception = null)
        => _proxyAuditBodyUnavailable(logger, reason, exception);

    public static void ProxyAuditBodyTruncated(this ILogger logger, int byteLimit)
        => _proxyAuditBodyTruncated(logger, byteLimit, null);

    public static void ProxyRetryAfter(this ILogger logger, string baseUrl, int retryAfterSeconds)
        => _proxyRetryAfter(logger, baseUrl, retryAfterSeconds, null);

    public static void ProxyError(this ILogger logger, string baseUrl, string message, Exception? exception = null)
        => _proxyError(logger, baseUrl, message, exception);

    public static void ProxyUpstreamError(this ILogger logger, string baseUrl, int statusCode)
        => _proxyUpstreamError(logger, baseUrl, statusCode, null);

    public static void ProxyUpstreamErrorWithBody(this ILogger logger, string baseUrl, int statusCode, string responseBody)
        => _proxyUpstreamErrorWithBody(logger, baseUrl, statusCode.ToString(), responseBody, null);

    public static void ProxyNoProviders(this ILogger logger)
        => _proxyNoProviders(logger, null);

    public static void ProxyUsage(this ILogger logger, string baseUrl, long elapsedMs, double promptTokensPerSec, double completionTokensPerSec)
        => _proxyUsage(logger, baseUrl, elapsedMs, promptTokensPerSec, completionTokensPerSec, null);

    public static void ProxyParsingUsage(this ILogger logger, string baseUrl)
        => _proxyParsingUsage(logger, baseUrl, null);

    public static void ProxyNoUsageData(this ILogger logger, string baseUrl, Exception? exception = null)
        => _proxyNoUsageData(logger, baseUrl, exception);

    public static void ProxySsePacket(this ILogger logger, string baseUrl, int payloadLength)
        => _proxySsePacket(logger, baseUrl, payloadLength, null);

    public static void ProxySseProgress(this ILogger logger, string baseUrl, int packetCount)
        => _proxySseProgress(logger, baseUrl, packetCount, null);
}
