using SunTrace.Core.Enums;

namespace SunTrace.Core.Models;

public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public AppLanguage Language { get; set; } = AppLanguage.System;

    public bool MonitoringEnabled { get; set; } = true;

    public int ScanIntervalMinutes { get; set; } = 15;

    public bool NotificationsEnabled { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public double WindowWidth { get; set; } = 1280;

    public double WindowHeight { get; set; } = 820;

    public double WindowLeft { get; set; } = double.NaN;

    public double WindowTop { get; set; } = double.NaN;

    public bool WindowMaximized { get; set; }

    public int SnapshotRetention { get; set; } = 40;

    public bool HasCompletedOnboarding { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public AppSettings Normalize()
    {
        if (!Enum.IsDefined(Language))
        {
            Language = AppLanguage.System;
        }

        if (ScanIntervalMinutes is not (5 or 15 or 30 or 60 or 120))
        {
            ScanIntervalMinutes = 15;
        }

        if (SnapshotRetention is < 10 or > 120)
        {
            SnapshotRetention = 40;
        }

        if (WindowWidth < 720)
        {
            WindowWidth = 1280;
        }

        if (WindowHeight < 560)
        {
            WindowHeight = 820;
        }

        return this;
    }
}
