using System.Windows;
using Microsoft.Win32;
using SunTrace.Core.Enums;

namespace SunTrace.App.Theme;

public sealed class ThemeManager
{
    private static readonly Uri LightUri = new("Themes/Palette.Light.xaml", UriKind.Relative);
    private static readonly Uri DarkUri = new("Themes/Palette.Dark.xaml", UriKind.Relative);
    private bool _watching;

    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    public bool IsDarkActive { get; private set; }

    public event EventHandler? ThemeChanged;

    public void Apply(ThemeMode mode)
    {
        Mode = mode;
        var dark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => IsWindowsDark()
        };

        ApplyPalette(dark);
        IsDarkActive = dark;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StartWatching()
    {
        if (_watching)
        {
            return;
        }

        _watching = true;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void StopWatching()
    {
        if (!_watching)
        {
            return;
        }

        _watching = false;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    public static bool IsWindowsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int number && number == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyPalette(bool dark)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        var dictionaries = application.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
        var existing = dictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString.Contains("Palette.", StringComparison.OrdinalIgnoreCase) == true);

        if (existing is not null)
        {
            var index = dictionaries.IndexOf(existing);
            dictionaries[index] = palette;
        }
        else
        {
            dictionaries.Insert(0, palette);
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle && Mode == ThemeMode.System)
        {
            Apply(ThemeMode.System);
        }
    }
}
