using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using SunTrace.Core.Models;
using SunTrace.Infrastructure.Windows;

namespace SunTrace.Infrastructure.Recovery;

public static partial class PauseController
{
    public static byte[] CreateDisabledValue()
    {
        var bytes = new byte[12];
        bytes[0] = 0x03;
        BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(bytes, 4);
        return bytes;
    }

    public static byte[] CreateEnabledValue()
    {
        var bytes = new byte[12];
        bytes[0] = 0x02;
        BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(bytes, 4);
        return bytes;
    }

    public static bool IsDisabled(byte[]? value) => value is { Length: > 0 } && value[0] is 3 or 7 or 9;

    public static string Signature(string? detail, bool enabled, string? triggers)
    {
        var raw = $"{detail}|{enabled}|{triggers}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static bool MatchesSignature(string? detail, bool enabled, string? triggers, string? expected)
    {
        if (string.IsNullOrEmpty(expected))
        {
            return false;
        }

        return string.Equals(Signature(detail, enabled, triggers), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[]? DecodeOriginal(PauseDescriptor descriptor) =>
        string.IsNullOrWhiteSpace(descriptor.OriginalApproveValueBase64)
            ? null
            : Convert.FromBase64String(descriptor.OriginalApproveValueBase64);

    private static bool IsMachinePath(string? path) =>
        path?.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase) == true;

    private static PauseDescriptor Copy(PauseDescriptor source) => new()
    {
        Kind = source.Kind,
        RegistryPath = source.RegistryPath,
        ApproveRegistryPath = source.ApproveRegistryPath,
        ValueName = source.ValueName,
        FilePath = source.FilePath,
        TaskPath = source.TaskPath,
        TaskName = source.TaskName,
        TaskFolder = source.TaskFolder,
        OriginalEnabled = source.OriginalEnabled,
        OriginalApproveValueExisted = source.OriginalApproveValueExisted,
        OriginalApproveValueBase64 = source.OriginalApproveValueBase64,
        OriginalDetail = source.OriginalDetail,
        OriginalSignature = source.OriginalSignature
    };
}
