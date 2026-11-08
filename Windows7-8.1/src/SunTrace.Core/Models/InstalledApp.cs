namespace SunTrace.Core.Models;

public sealed class InstalledApp
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Publisher { get; set; }

    public string? Version { get; set; }

    public DateTimeOffset? InstallDate { get; set; }

    public DateTimeOffset? LastUpdated { get; set; }

    public string? InstallLocation { get; set; }

    public string? UninstallString { get; set; }

    public string? DisplayIcon { get; set; }

    public string? MainExecutablePath { get; set; }

    public string? RegistryKey { get; set; }

    public string? SignaturePublisher { get; set; }

    public long? EstimatedSizeBytes { get; set; }

    public bool IsSystemComponent { get; set; }

    public string Source { get; set; } = "Реестр";

    public string DisplayPublisher => string.IsNullOrWhiteSpace(Publisher) ? "Издатель не указан" : Publisher!;

    public string DisplayVersion => string.IsNullOrWhiteSpace(Version) ? "Версия не указана" : Version!;
}
