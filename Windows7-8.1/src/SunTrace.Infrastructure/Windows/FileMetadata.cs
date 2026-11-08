using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace SunTrace.Infrastructure.Windows;

public sealed class FileMetadataReader
{
    private readonly ConcurrentDictionary<string, (DateTime Stamp, string? Company, string? Description)> _versionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SignatureReader _signatureReader;

    public FileMetadataReader(SignatureReader signatureReader)
    {
        _signatureReader = signatureReader;
    }

    public (string? Company, string? Description) ReadVersionInfo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return (null, null);
        }

        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            if (_versionCache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
            {
                return (cached.Company, cached.Description);
            }

            var info = FileVersionInfo.GetVersionInfo(path);
            var company = string.IsNullOrWhiteSpace(info.CompanyName) ? null : info.CompanyName.Trim();
            var description = string.IsNullOrWhiteSpace(info.FileDescription) ? null : info.FileDescription.Trim();
            _versionCache[path] = (stamp, company, description);
            return (company, description);
        }
        catch
        {
            return (null, null);
        }
    }

    public string? ReadSignaturePublisher(string? path) => _signatureReader.GetPublisher(path);
}

public sealed class SignatureReader
{
    private readonly ConcurrentDictionary<string, (DateTime Stamp, string? Publisher)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public string? GetPublisher(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        DateTime stamp;
        try
        {
            stamp = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception)
        {
            return null;
        }

        if (_cache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
        {
            return cached.Publisher;
        }

        string? normalized = null;
        try
        {
#if NET6_0
            using var certificate = new X509Certificate2(path);
#else
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(path);
#endif
            var publisher = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            normalized = string.IsNullOrWhiteSpace(publisher) ? null : publisher.Trim();
        }
        catch (Exception)
        {
        }

        _cache[path] = (stamp, normalized);
        return normalized;
    }
}
