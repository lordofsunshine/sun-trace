using System.ComponentModel;
using System.Globalization;
using System.Resources;
using SunTrace.Core.Enums;

namespace SunTrace.App.Localization;

public sealed class LocalizedStrings : INotifyPropertyChanged
{
    private static readonly string[] CisLanguages = ["ru", "be", "kk", "ky", "tg", "tk", "uk", "uz", "az", "hy", "ka", "mo"];
    private static readonly Dictionary<string, ResourceManager> Managers = new(StringComparer.OrdinalIgnoreCase);
    private CultureInfo _culture = CultureInfo.GetCultureInfo("ru-RU");

    public static LocalizedStrings Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public CultureInfo Culture => _culture;

    public string this[string key] => Get(key);

    public static AppLanguage DetectSystemLanguage()
    {
        var language = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        return language.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.English
            : CisLanguages.Contains(language, StringComparer.OrdinalIgnoreCase)
                ? AppLanguage.Russian
                : AppLanguage.English;
    }

    public void Apply(AppLanguage language)
    {
        var effectiveLanguage = language == AppLanguage.System ? DetectSystemLanguage() : language;
        var culture = CultureInfo.GetCultureInfo(effectiveLanguage == AppLanguage.Russian ? "ru-RU" : "en-US");
        var changed = !Equals(_culture, culture);
        _culture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        if (changed)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string Get(string key)
    {
        var separator = key.IndexOf('.');
        if (separator <= 0 || separator == key.Length - 1)
        {
            return key;
        }

        var category = key[..separator];
        var name = key[(separator + 1)..];
        if (!Managers.TryGetValue(category, out var manager))
        {
            manager = new ResourceManager($"SunTrace.App.Localization.{category}", typeof(LocalizedStrings).Assembly);
            Managers[category] = manager;
        }

        return manager.GetString(name, _culture) ?? key;
    }
}
