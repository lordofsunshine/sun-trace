using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SunTrace.App.Localization;
using SunTrace.Core.Detection;

namespace SunTrace.App.Converters;

public sealed class RelativeTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset time)
        {
            return string.Empty;
        }

        var local = time.ToLocalTime();
        var ru = LocalizedStrings.Instance.Culture;
        var today = DateTimeOffset.Now.Date;
        if (local.Date == today)
        {
            return LocalizedStrings.Instance["Ui.Today"] + ", " + local.ToString("HH:mm", ru);
        }

        if (local.Date == today.AddDays(-1))
        {
            return LocalizedStrings.Instance["Ui.Yesterday"] + ", " + local.ToString("HH:mm", ru);
        }

        return local.Year == today.Year
            ? local.ToString("d MMMM, HH:mm", ru)
            : local.ToString("d MMMM yyyy, HH:mm", ru);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class FriendlyDateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset time)
        {
            return LocalizedStrings.Instance["Ui.NoChangesYet"];
        }

        var local = time.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        var ru = LocalizedStrings.Instance.Culture;
        if (local.Date == today)
        {
            return LocalizedStrings.Instance["Ui.Today"] + ", " + local.ToString("HH:mm", ru);
        }

        if (local.Date == today.AddDays(-1))
        {
            return LocalizedStrings.Instance["Ui.Yesterday"];
        }

        var days = (today - local.Date).Days;
        if (days is > 0 and < 7)
        {
            return string.Format(LocalizedStrings.Instance["Ui.DaysAgoFormat"], days);
        }

        return local.Year == today.Year
            ? local.ToString("d MMMM", ru)
            : local.ToString("d MMMM yyyy", ru);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class DayLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var date = value switch
        {
            DateTimeOffset offset => offset.ToLocalTime().Date,
            DateTime time => time.Date,
            _ => DateTime.Today
        };

        var today = DateTime.Today;
        if (date == today)
        {
            return LocalizedStrings.Instance["Ui.Today"];
        }

        return date == today.AddDays(-1)
            ? LocalizedStrings.Instance["Ui.Yesterday"]
            : date.ToString("d MMMM yyyy", LocalizedStrings.Instance.Culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class FileSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long bytes ? SnapshotComparer.FormatSize(bytes) : "-";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
