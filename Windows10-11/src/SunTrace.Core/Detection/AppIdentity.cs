using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SunTrace.Core.Models;

namespace SunTrace.Core.Detection;

public static partial class AppIdentity
{
    private static readonly string[] PublisherSuffixes =
    [
        "incorporated", "inc", "llc", "ltd", "limited", "corp", "corporation", "co", "gmbh", "sa", "sas", "bv", "ab", "oy", "plc", "company", "technologies", "software"
    ];

    private static readonly string[] NoiseWords =
    [
        "microsoft", "windows", "the", "for", "and", "with", "app", "application", "desktop", "client", "version", "edition"
    ];

    public static string CreateId(InstalledApp app)
    {
        var name = NormalizeName(app.Name);
        var publisher = NormalizePublisher(app.Publisher);
        var raw = string.IsNullOrEmpty(publisher) ? name : $"{name}|{publisher}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16].ToLowerInvariant();
        return "app-" + hash;
    }

    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var value = name.ToLowerInvariant();
        value = BracketsRegex().Replace(value, " ");
        value = VersionRegex().Replace(value, " ");
        value = value.Replace("(x64)", " ").Replace("(x86)", " ").Replace("64-bit", " ").Replace("32-bit", " ");
        value = NonAlphaNumericRegex().Replace(value, " ");
        return WhitespaceRegex().Replace(value, " ").Trim();
    }

    public static string NormalizePublisher(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher))
        {
            return string.Empty;
        }

        var value = publisher.ToLowerInvariant();
        value = NonAlphaNumericRegex().Replace(value, " ");
        var tokens = WhitespaceRegex().Replace(value, " ").Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !PublisherSuffixes.Contains(t))
            .ToArray();
        return string.Join(' ', tokens);
    }

    public static bool PublishersMatch(string? first, string? second)
    {
        var a = NormalizePublisher(first);
        var b = NormalizePublisher(second);
        if (a.Length < 3 || b.Length < 3)
        {
            return false;
        }

        return a == b || a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal);
    }

    public static bool NamesRelated(string? firstName, string? secondName)
    {
        var a = NormalizeName(firstName);
        var b = NormalizeName(secondName);
        if (a.Length < 4 || b.Length < 4)
        {
            return false;
        }

        if (a == b || a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
        {
            return true;
        }

        var tokensA = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length > 2 && !NoiseWords.Contains(t)).ToHashSet();
        var tokensB = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length > 2 && !NoiseWords.Contains(t)).ToHashSet();
        if (tokensA.Count == 0 || tokensB.Count == 0)
        {
            return false;
        }

        var shared = tokensA.Intersect(tokensB).Count();
        var smaller = Math.Min(tokensA.Count, tokensB.Count);
        return shared >= 1 && (shared >= 2 || smaller == 1 || shared * 100 / smaller >= 60);
    }

    public static bool AreSameProduct(InstalledApp first, InstalledApp second)
    {
        if (first.Id == second.Id)
        {
            return true;
        }

        if (!NamesRelated(first.Name, second.Name))
        {
            return false;
        }

        if (PublishersMatch(first.Publisher, second.Publisher) || PublishersMatch(first.SignaturePublisher, second.SignaturePublisher))
        {
            return true;
        }

        var locationA = NormalizedPath(first.InstallLocation);
        var locationB = NormalizedPath(second.InstallLocation);
        return !string.IsNullOrEmpty(locationA) && locationA == locationB;
    }

    public static string? NormalizedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var value = path.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
        if (value.EndsWith(".exe", StringComparison.Ordinal) || value.EndsWith(".ico", StringComparison.Ordinal))
        {
            var index = value.LastIndexOf('\\');
            value = index > 0 ? value[..index] : value;
        }

        return value;
    }

    public static string FormatDate(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU")) ?? "не указана";

    public static string FormatDateTime(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("d MMMM yyyy, HH:mm", CultureInfo.GetCultureInfo("ru-RU")) ?? "не указано";

#if NET7_0_OR_GREATER
    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex BracketsRegex();

    [GeneratedRegex(@"\b\d+(\.\d+){1,4}\b")]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"[^a-zа-я0-9]+")]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
#else
    private static readonly Regex BracketsPattern = new(@"\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex VersionPattern = new(@"\b\d+(\.\d+){1,4}\b", RegexOptions.Compiled);
    private static readonly Regex NonAlphaNumericPattern = new(@"[^a-zа-я0-9]+", RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    private static Regex BracketsRegex() => BracketsPattern;

    private static Regex VersionRegex() => VersionPattern;

    private static Regex NonAlphaNumericRegex() => NonAlphaNumericPattern;

    private static Regex WhitespaceRegex() => WhitespacePattern;
#endif
}
