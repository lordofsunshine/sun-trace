namespace SunTrace.Infrastructure;

public sealed class DataPaths
{
    public DataPaths(string? rootOverride = null)
    {
        Root = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SunTrace");

        DatabasePath = Path.Combine(Root, "database.db");
        BackupsDirectory = Path.Combine(Root, "backups");
        LogsDirectory = Path.Combine(Root, "logs");
        ExportsDirectory = Path.Combine(Root, "exports");
        IconCacheDirectory = Path.Combine(Root, "iconcache");
        SettingsPath = Path.Combine(Root, "settings.json");
    }

    public string Root { get; }

    public string DatabasePath { get; }

    public string BackupsDirectory { get; }

    public string LogsDirectory { get; }

    public string ExportsDirectory { get; }

    public string IconCacheDirectory { get; }

    public string SettingsPath { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ExportsDirectory);
        Directory.CreateDirectory(IconCacheDirectory);
    }
}
