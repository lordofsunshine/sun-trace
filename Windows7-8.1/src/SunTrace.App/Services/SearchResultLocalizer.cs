using SunTrace.App.Localization;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.App.Services;

public static class SearchResultLocalizer
{
    public static SearchResult Localize(SearchResult result)
    {
        if (!LocalizedStrings.Instance.Culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        return new SearchResult
        {
            Kind = result.Kind,
            Title = result.Kind == SearchResultKind.Event ? EventTitle(result.Title) : result.Title,
            Subtitle = result.Kind switch
            {
                SearchResultKind.Application => ApplicationSubtitle(result.Subtitle),
                SearchResultKind.Event => result.Subtitle.Replace("Связь не определена", Text("Event.SearchLinkUnknown"), StringComparison.Ordinal),
                SearchResultKind.Component => ComponentSubtitle(result.Subtitle),
                _ => result.Subtitle
            },
            TargetId = result.TargetId,
            AppId = result.AppId
        };
    }

    private static string ApplicationSubtitle(string subtitle)
    {
        var localized = subtitle.Replace("Издатель не указан", Text("Event.SearchPublisherUnknown"), StringComparison.Ordinal)
            .Replace("версия не указана", Text("Event.SearchVersionUnknown"), StringComparison.Ordinal);
        return localized == "Программа удалена · сохранена история"
            ? Text("Event.SearchRemovedWithHistory")
            : localized;
    }

    private static string ComponentSubtitle(string subtitle)
    {
        if (subtitle.StartsWith("Элемент автозагрузки", StringComparison.Ordinal))
        {
            return Text("Event.SearchStartupItem") + subtitle["Элемент автозагрузки".Length..];
        }

        if (subtitle.StartsWith("Служба Windows", StringComparison.Ordinal))
        {
            return Text("Event.SearchWindowsService") + subtitle["Служба Windows".Length..];
        }

        if (subtitle.StartsWith("Задание планировщика", StringComparison.Ordinal))
        {
            return Text("Event.SearchScheduledTask") + subtitle["Задание планировщика".Length..];
        }

        if (subtitle.StartsWith("Файл программы", StringComparison.Ordinal))
        {
            return Text("Event.SearchApplicationFile") + subtitle["Файл программы".Length..];
        }

        if (subtitle.StartsWith("Папка программы", StringComparison.Ordinal))
        {
            return Text("Event.SearchApplicationFolder") + subtitle["Папка программы".Length..];
        }

        if (subtitle.StartsWith("Компонент", StringComparison.Ordinal))
        {
            return Text("Event.SearchComponent") + subtitle["Компонент".Length..];
        }

        return subtitle;
    }

    private static string EventTitle(string title) => title switch
    {
        "Программа установлена" => Text("Event.TitleAppInstalled"),
        "Программа удалена" => Text("Event.TitleAppRemoved"),
        "Обновление программы" => Text("Event.TitleAppUpdated"),
        "Изменены параметры программы" => Text("Event.TitleAppChanged"),
        "Новый элемент автозагрузки" => Text("Event.TitleStartupAdded"),
        "Элемент автозагрузки удалён" => Text("Event.TitleStartupRemoved"),
        "Элемент автозагрузки включён" => Text("Event.TitleStartupEnabled"),
        "Изменены параметры автозагрузки" => Text("Event.TitleStartupChanged"),
        "Новая фоновая служба" => Text("Event.TitleServiceAdded"),
        "Служба удалена" => Text("Event.TitleServiceRemoved"),
        "Изменены параметры службы" => Text("Event.TitleServiceChanged"),
        "Новое задание планировщика" => Text("Event.TitleTaskAdded"),
        "Задание планировщика удалено" => Text("Event.TitleTaskRemoved"),
        "Задание планировщика включено" => Text("Event.TitleTaskEnabled"),
        "Изменены параметры задания" => Text("Event.TitleTaskChanged"),
        "Новые файлы программы" => Text("Event.TitleFilesAdded"),
        "Новые папки программы" => Text("Event.TitleFoldersAdded"),
        "Файлы программы удалены" => Text("Event.TitleFilesRemoved"),
        "Изменены файлы программы" => Text("Event.TitleFilesChanged"),
        "Компонент временно отключён" => Text("Event.TitlePaused"),
        "Компонент восстановлен" => Text("Event.TitleRestored"),
        "Обнаружено изменение" => Text("Event.TitleChangeDetected"),
        _ => title
    };

    private static string Text(string key) => LocalizedStrings.Instance.Get(key);
}
