using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SunTrace.App.Localization;
using SunTrace.App.Theme;
using SunTrace.Core.Enums;

namespace SunTrace.App.Converters;

public sealed class AppStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        UiBrushes.ForStatus(value is AppStatus status ? status : AppStatus.Unchanged);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class AppStatusToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is AppStatus status ? status : AppStatus.Unchanged) switch
        {
            AppStatus.Updated => LocalizedStrings.Instance["Ui.StatusUpdated"],
            AppStatus.Changed => LocalizedStrings.Instance["Ui.StatusChanged"],
            AppStatus.Attention => LocalizedStrings.Instance["Ui.StatusAttention"],
            AppStatus.PausedComponents => LocalizedStrings.Instance["Ui.StatusPaused"],
            AppStatus.New => LocalizedStrings.Instance["Ui.StatusNew"],
            _ => LocalizedStrings.Instance["Ui.StatusUnchanged"]
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ConfidenceToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        UiBrushes.ForConfidence(value is ConfidenceLevel level ? level : ConfidenceLevel.Unknown);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ConfidenceToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ConfidenceLevel.High => LocalizedStrings.Instance["Ui.ConfidenceHigh"],
        ConfidenceLevel.Medium => LocalizedStrings.Instance["Ui.ConfidenceMedium"],
        ConfidenceLevel.Low => LocalizedStrings.Instance["Ui.ConfidenceLow"],
        _ => LocalizedStrings.Instance["Ui.ConfidenceUnknown"]
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ImportanceToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        UiBrushes.ForImportance(value is ImportanceLevel level ? level : ImportanceLevel.Normal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ImportanceToSoftBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        UiBrushes.ForImportanceSoft(value is ImportanceLevel level ? level : ImportanceLevel.Normal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ChangeKindToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ChangeKind.Added => LocalizedStrings.Instance["Ui.KindAdded"],
        ChangeKind.Removed => LocalizedStrings.Instance["Ui.KindRemoved"],
        ChangeKind.Modified => LocalizedStrings.Instance["Ui.KindModified"],
        ChangeKind.Paused => LocalizedStrings.Instance["Ui.KindPaused"],
        ChangeKind.Restored => LocalizedStrings.Instance["Ui.KindRestored"],
        _ => LocalizedStrings.Instance["Ui.KindUnchanged"]
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ImportanceToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ImportanceLevel.Attention => LocalizedStrings.Instance["Ui.ImportanceAttention"],
        ImportanceLevel.Check => LocalizedStrings.Instance["Ui.ImportanceCheck"],
        _ => LocalizedStrings.Instance["Ui.ImportanceNormal"]
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class PauseStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        UiBrushes.ForPauseStatus(value is PauseStatus status ? status : PauseStatus.Active);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class PauseStatusToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        PauseStatus.Active => LocalizedStrings.Instance["Ui.PauseActive"],
        PauseStatus.Restored => LocalizedStrings.Instance["Ui.PauseRestored"],
        PauseStatus.Conflict => LocalizedStrings.Instance["Ui.PauseConflict"],
        PauseStatus.Failed => LocalizedStrings.Instance["Ui.PauseFailed"],
        PauseStatus.TargetMissing => LocalizedStrings.Instance["Ui.PauseMissing"],
        PauseStatus.KeptCurrent => LocalizedStrings.Instance["Ui.PauseKept"],
        _ => LocalizedStrings.Instance["Ui.Unknown"]
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class KindToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        UiBrushes.ForKind(value is ChangeKind kind ? kind : ChangeKind.Unchanged);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
