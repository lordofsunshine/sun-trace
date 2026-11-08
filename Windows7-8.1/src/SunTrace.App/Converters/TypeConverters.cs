using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SunTrace.App.Localization;
using SunTrace.Core.Enums;

namespace SunTrace.App.Converters;

public sealed class ComponentTypeToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ComponentType.Application => LocalizedStrings.Instance["Ui.ComponentProgram"],
        ComponentType.Startup => LocalizedStrings.Instance["Ui.ComponentStartup"],
        ComponentType.Service => LocalizedStrings.Instance["Ui.ComponentService"],
        ComponentType.ScheduledTask => LocalizedStrings.Instance["Ui.ComponentTask"],
        ComponentType.File => LocalizedStrings.Instance["Ui.ComponentFile"],
        ComponentType.Folder => LocalizedStrings.Instance["Ui.ComponentFolder"],
        ComponentType.Registry => LocalizedStrings.Instance["Ui.ComponentRegistry"],
        _ => LocalizedStrings.Instance["Ui.ComponentGeneric"]
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ComponentTypeToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            ComponentType.Startup => "Icon.Startup",
            ComponentType.Service => "Icon.Service",
            ComponentType.ScheduledTask => "Icon.Task",
            ComponentType.File => "Icon.File",
            ComponentType.Folder => "Icon.Folder",
            ComponentType.Registry => "Icon.Registry",
            ComponentType.Application => "Icon.Apps",
            _ => "Icon.Package"
        };

        return Application.Current?.TryFindResource(key) as Geometry ?? Geometry.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class SearchKindToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            SearchResultKind.Application => "Icon.Apps",
            SearchResultKind.Event => "Icon.History",
            SearchResultKind.Component => "Icon.Package",
            _ => "Icon.Info"
        };

        return Application.Current?.TryFindResource(key) as Geometry ?? Geometry.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
