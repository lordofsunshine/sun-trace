using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Storage;

internal static class StorageJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string? json) =>
        string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Options);
}

internal static class DbTime
{
    public static string Now => ToDb(DateTimeOffset.Now);

    public static string ToDb(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static DateTimeOffset? FromDb(object? value)
    {
        if (value is null || value is DBNull)
        {
            return null;
        }

        var text = value.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}

internal static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SunTrace.SafePause.v1");

    public static string Protect(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return "dpapi:" + Convert.ToBase64String(protectedBytes);
    }

    public static string? Unprotect(string? blob)
    {
        if (string.IsNullOrWhiteSpace(blob))
        {
            return null;
        }

        try
        {
            if (blob.StartsWith("plain:", StringComparison.Ordinal))
            {
                return null;
            }

            var payload = blob.StartsWith("dpapi:", StringComparison.Ordinal) ? blob[6..] : blob;
            var bytes = Convert.FromBase64String(payload);
            var plain = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }
}

internal static class Hashing
{
    public static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string Sha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
