using System.Windows.Media;
using SunTrace.Core.Enums;

namespace SunTrace.App.Theme;

public static class UiBrushes
{
    public static readonly SolidColorBrush Neutral = Create("#8A9096");
    public static readonly SolidColorBrush NeutralSoft = Create("#248A9096");
    public static readonly SolidColorBrush Success = Create("#2F8F5B");
    public static readonly SolidColorBrush SuccessSoft = Create("#252F8F5B");
    public static readonly SolidColorBrush Warning = Create("#B9821F");
    public static readonly SolidColorBrush WarningSoft = Create("#28B9821F");
    public static readonly SolidColorBrush Danger = Create("#C05045");
    public static readonly SolidColorBrush DangerSoft = Create("#28C05045");
    public static readonly SolidColorBrush Accent = Create("#E8932B");
    public static readonly SolidColorBrush AccentSoft = Create("#28E8932B");

    public static SolidColorBrush ForStatus(AppStatus status) => status switch
    {
        AppStatus.Updated => Success,
        AppStatus.New => Success,
        AppStatus.Changed => Warning,
        AppStatus.Attention => Danger,
        AppStatus.PausedComponents => Neutral,
        _ => Neutral
    };

    public static SolidColorBrush ForConfidence(ConfidenceLevel level) => level switch
    {
        ConfidenceLevel.High => Success,
        ConfidenceLevel.Medium => Warning,
        ConfidenceLevel.Low => Neutral,
        _ => Neutral
    };

    public static SolidColorBrush ForImportance(ImportanceLevel level) => level switch
    {
        ImportanceLevel.Attention => Danger,
        ImportanceLevel.Check => Warning,
        _ => Neutral
    };

    public static SolidColorBrush ForImportanceSoft(ImportanceLevel level) => level switch
    {
        ImportanceLevel.Attention => DangerSoft,
        ImportanceLevel.Check => WarningSoft,
        _ => NeutralSoft
    };

    public static SolidColorBrush ForPauseStatus(PauseStatus status) => status switch
    {
        PauseStatus.Active => Warning,
        PauseStatus.Restored => Success,
        PauseStatus.Conflict => Danger,
        PauseStatus.Failed => Danger,
        PauseStatus.TargetMissing => Warning,
        PauseStatus.KeptCurrent => Neutral,
        _ => Neutral
    };

    public static SolidColorBrush ForKind(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => Success,
        ChangeKind.Restored => Success,
        ChangeKind.Removed => Danger,
        ChangeKind.Modified => Warning,
        ChangeKind.Paused => Neutral,
        _ => Neutral
    };

    private static SolidColorBrush Create(string color)
    {
        var brush = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
