using System.Globalization;
using System.Resources;

namespace SunTrace.Infrastructure.Localization;

internal static class ReportText
{
    private static readonly ResourceManager Manager = new("SunTrace.Infrastructure.Localization.Report", typeof(ReportText).Assembly);

    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), args);
}
