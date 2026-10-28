namespace LlmProxy.Application.Services;

using System.Security.Cryptography;
using System.Text.Json;

/// <summary>
/// Fingerprints JSON content so equal content can be recognised again later, no matter how it was written.
/// </summary>
/// <remarks>
/// The fingerprint is taken over a canonical rendering — object keys sorted recursively, arrays kept in order —
/// hashed with SHA-256 and shortened to its first 16 lowercase hex characters. Odd input never throws:
/// undefined and null values have no fingerprint and anything unrenderable yields <c>null</c> as well.
/// </remarks>
internal static class JsonFingerprint
{
    private const int HexLength = 16;

    /// <summary>Computes the stable fingerprint of a JSON element.</summary>
    /// <param name="element">Element to fingerprint.</param>
    /// <returns>First 16 lowercase hex characters of the SHA-256 hash, or null for undefined and null values.</returns>
    internal static string? Of(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteCanonical(writer, element);
            }

            // The writer already produced UTF-8 bytes, so the hash is taken over them directly.
            var hex = Convert.ToHexString(SHA256.HashData(stream.ToArray()));

            return hex[..HexLength].ToLowerInvariant();
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            return null;
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }
}
