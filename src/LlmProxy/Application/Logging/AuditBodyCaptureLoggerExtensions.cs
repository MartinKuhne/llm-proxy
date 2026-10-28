namespace LlmProxy.Application.Logging;

using Microsoft.Extensions.Logging;

/// <summary>
/// Semantic logging events for audit body capture (event ids 20 and 21, kept from the proxy events they were cut from).
/// </summary>
public static class AuditBodyCaptureLoggerExtensions
{
    private static readonly Action<ILogger, string, Exception?> _auditBodyUnavailable =
        LoggerMessage.Define<string>(LogLevel.Debug, 20, "Could not capture the full request body for the audit log: {Reason}");

    private static readonly Action<ILogger, int, Exception?> _auditBodyTruncated =
        LoggerMessage.Define<int>(LogLevel.Debug, 21, "Audit copy of the request body truncated at {ByteLimit} bytes");

    public static void AuditBodyUnavailable(this ILogger logger, string reason, Exception? exception = null)
        => _auditBodyUnavailable(logger, reason, exception);

    public static void AuditBodyTruncated(this ILogger logger, int byteLimit)
        => _auditBodyTruncated(logger, byteLimit, null);
}
