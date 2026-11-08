using SunTrace.Core.Enums;

namespace SunTrace.Core.Models;

public sealed class SystemSnapshot
{
    public long Id { get; set; }

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;

    public ScanReason Reason { get; set; } = ScanReason.Manual;

    public List<InstalledApp> Apps { get; set; } = new();

    public List<ComponentSnapshot> Components { get; set; } = new();

    public Dictionary<ScanSection, SectionReport> Sections { get; set; } = new();

    public long? SessionId { get; set; }

    public bool IsComplete => Sections.Count > 0 && Sections.Values.All(s => s.Status == SectionStatus.Complete);

    public SectionReport GetSection(ScanSection section) =>
        Sections.TryGetValue(section, out var report) ? report : new SectionReport { Section = section, Status = SectionStatus.Unavailable };

    public int ComponentCount => Components.Count;
}

public sealed class SectionReport
{
    public ScanSection Section { get; set; }

    public SectionStatus Status { get; set; } = SectionStatus.Complete;

    public int ItemsScanned { get; set; }

    public string? Message { get; set; }

    public bool WasSkipped => Status != SectionStatus.Complete;
}

public sealed class SnapshotSummary
{
    public long Id { get; set; }

    public DateTimeOffset CapturedAt { get; set; }

    public ScanReason Reason { get; set; }

    public int AppCount { get; set; }

    public int ComponentCount { get; set; }

    public bool IsComplete { get; set; }

    public string ReasonLabel => Reason switch
    {
        ScanReason.Baseline => "Базовый снимок",
        ScanReason.Manual => "Ручное сканирование",
        ScanReason.Monitoring => "Фоновая проверка",
        ScanReason.TrackingResult => "Завершение отслеживания",
        _ => "Снимок"
    };
}

public sealed class ScanProgress
{
    public ScanSection Section { get; init; }

    public string Message { get; init; } = string.Empty;

    public int CompletedSections { get; init; }

    public int TotalSections { get; init; }

    public bool IsFinished { get; init; }

    public double Percentage => TotalSections <= 0 ? 0 : Math.Min(100, CompletedSections * 100d / TotalSections);
}

public sealed class ScanOutcome
{
    public SystemSnapshot Snapshot { get; init; } = new();

    public long SnapshotId { get; init; }

    public SnapshotDiff? Diff { get; init; }

    public IReadOnlyList<ChangeEvent> Events { get; init; } = Array.Empty<ChangeEvent>();

    public bool IsFirstScan { get; init; }

    public string? FirstScanMessage { get; init; }
}
