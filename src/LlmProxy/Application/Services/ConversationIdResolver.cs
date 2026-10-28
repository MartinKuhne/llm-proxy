namespace LlmProxy.Application.Services;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LlmProxy.Application.Logging;

/// <summary>
/// Decides which conversation a request belongs to: an explicit client header wins, otherwise the conversation
/// opening (first system plus first user message) is hashed so that replayed history lands in the same file.
/// </summary>
/// <remarks>
/// Message access is defensive: content may be a plain string or an array of content parts, and oddly shaped
/// bodies fall back to a fresh id rather than throwing into the proxy path.
/// </remarks>
public class ConversationIdResolver
{
    /// <summary>Request header a client can use to name its conversation explicitly.</summary>
    public const string HeaderName = "X-Conversation-Id";

    private const int DerivedLength = 16;
    private const char PartSeparator = '\u0000';

    /// <summary>The shape a client-supplied conversation id must have to be honoured.</summary>
    private static readonly Regex _clientIdPattern = new("^[A-Za-z0-9._-]{1,100}$", RegexOptions.Compiled);

    private readonly ILogger<ConversationIdResolver> _logger;

    public ConversationIdResolver(ILogger<ConversationIdResolver> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Resolves the conversation id for a request from the <see cref="HeaderName"/> header or the request body.
    /// </summary>
    /// <param name="request">Incoming request.</param>
    /// <param name="requestBody">Parsed request body, when one was available.</param>
    /// <returns>A non-empty conversation id.</returns>
    /// <remarks>
    /// A header value is honoured only when it matches <see cref="_clientIdPattern"/>. Taking the client's string
    /// verbatim would let anyone append events to a conversation they do not own by sending a known or guessed id,
    /// and would fold two distinct ids onto one file: "a/b", "a\b" and "a_b" all sanitize to "a_b". Path traversal
    /// is already neutralised by the file naming; what is protected here is identity integrity. A value that does
    /// not conform is ignored and the request derives its own conversation as if no header had been sent.
    /// </remarks>
    public string Resolve(HttpRequest request, JsonElement? requestBody)
    {
        if (request.Headers.TryGetValue(HeaderName, out var header))
        {
            var value = header.ToString().Trim();
            if (value.Length > 0)
            {
                if (_clientIdPattern.IsMatch(value))
                {
                    return value;
                }

                // The value itself is not logged: it is client content and may carry what it was trying to hide.
                _logger.ConversationIdHeaderRejected(value.Length);
            }
        }

        return DeriveFromMessages(requestBody);
    }

    /// <summary>
    /// Derives a stable conversation id from the first system and first user message of a request body:
    /// the first 16 hex characters of <c>SHA-256(system + U+0000 + user)</c>.
    /// </summary>
    /// <param name="requestBody">Parsed request body, when one was available.</param>
    /// <returns>A derived id, or a fresh guid when the body carries neither a system nor a user message.</returns>
    public static string DeriveFromMessages(JsonElement? requestBody)
    {
        var system = FindFirstMessage(requestBody, "system");
        var user = FindFirstMessage(requestBody, "user");

        if (system is null && user is null)
        {
            return Guid.NewGuid().ToString("N");
        }

        var seed = string.Concat(system ?? string.Empty, PartSeparator, user ?? string.Empty);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexStringLower(hash)[..DerivedLength];
    }

    private static string? FindFirstMessage(JsonElement? requestBody, string role)
    {
        if (requestBody?.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var root = requestBody.Value;
        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var message in messages.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object ||
                !message.TryGetProperty("role", out var actual) || actual.ValueKind != JsonValueKind.String ||
                !string.Equals(actual.GetString(), role, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return ExtractText(message);
        }

        return null;
    }

    private static string ExtractText(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        // Plain string content, or the OpenAI content-part array: concatenate every text part.
        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.Object &&
                part.TryGetProperty("text", out var partText) && partText.ValueKind == JsonValueKind.String)
            {
                text.Append(partText.GetString());
            }
        }

        return text.ToString();
    }
}
