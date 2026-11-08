using SunTrace.App.Localization;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.App.Services;

public static class ChangeEventLocalizer
{
    public static ChangeEvent Localize(ChangeEvent item)
    {
        if (!LocalizedStrings.Instance.Culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            return item;
        }

        return new ChangeEvent
        {
            Id = item.Id,
            Fingerprint = item.Fingerprint,
            AppId = item.AppId,
            AppName = item.AppName,
            Kind = item.Kind,
            ComponentType = item.ComponentType,
            ComponentName = item.ComponentName,
            Title = BuildTitle(item),
            Description = BuildDescription(item),
            Details = BuildDetails(item),
            Confidence = item.Confidence,
            ConfidenceReasons = item.ConfidenceReasons,
            Importance = item.Importance,
            Source = item.Source,
            DetectedAt = item.DetectedAt,
            SnapshotId = item.SnapshotId,
            SessionId = item.SessionId,
            IsReverted = item.IsReverted,
            IsRead = item.IsRead,
            AffectedCount = item.AffectedCount
        };
    }

    private static string BuildTitle(ChangeEvent item)
    {
        if (item.Kind == ChangeKind.Paused)
        {
            return Text("Event.TitlePaused");
        }

        if (item.Kind == ChangeKind.Restored)
        {
            return Text("Event.TitleRestored");
        }

        return (item.Kind, item.ComponentType) switch
        {
            (ChangeKind.Added, ComponentType.Application) => Text("Event.TitleAppInstalled"),
            (ChangeKind.Removed, ComponentType.Application) => Text("Event.TitleAppRemoved"),
            (ChangeKind.Modified, ComponentType.Application) => IsVersionChange(item)
                ? Text("Event.TitleAppUpdated")
                : Text("Event.TitleAppChanged"),
            (ChangeKind.Added, ComponentType.Startup) => Text("Event.TitleStartupAdded"),
            (ChangeKind.Removed, ComponentType.Startup) => Text("Event.TitleStartupRemoved"),
            (ChangeKind.Modified, ComponentType.Startup) => IsEnabledTransition(item)
                ? Text("Event.TitleStartupEnabled")
                : Text("Event.TitleStartupChanged"),
            (ChangeKind.Added, ComponentType.Service) => Text("Event.TitleServiceAdded"),
            (ChangeKind.Removed, ComponentType.Service) => Text("Event.TitleServiceRemoved"),
            (ChangeKind.Modified, ComponentType.Service) => Text("Event.TitleServiceChanged"),
            (ChangeKind.Added, ComponentType.ScheduledTask) => Text("Event.TitleTaskAdded"),
            (ChangeKind.Removed, ComponentType.ScheduledTask) => Text("Event.TitleTaskRemoved"),
            (ChangeKind.Modified, ComponentType.ScheduledTask) => IsEnabledTransition(item)
                ? Text("Event.TitleTaskEnabled")
                : Text("Event.TitleTaskChanged"),
            (ChangeKind.Added, ComponentType.Folder) => Text("Event.TitleFoldersAdded"),
            (ChangeKind.Removed, ComponentType.Folder) => Text("Event.TitleFoldersRemoved"),
            (ChangeKind.Modified, ComponentType.Folder) => Text("Event.TitleFoldersChanged"),
            (ChangeKind.Added, ComponentType.File) => Text("Event.TitleFilesAdded"),
            (ChangeKind.Removed, ComponentType.File) => Text("Event.TitleFilesRemoved"),
            (ChangeKind.Modified, ComponentType.File) => Text("Event.TitleFilesChanged"),
            _ => Text("Event.TitleChangeDetected")
        };
    }

    private static string BuildDescription(ChangeEvent item)
    {
        if (item.Kind == ChangeKind.Paused)
        {
            return Format("Event.DescPaused", Name(item));
        }

        if (item.Kind == ChangeKind.Restored)
        {
            return Format("Event.DescRestored", Name(item));
        }

        var name = Name(item);
        var version = FieldAfter(item, "Версия") ?? FieldBefore(item, "Версия") ?? "-";
        var beforeVersion = FieldBefore(item, "Версия") ?? "-";
        var count = Count(item);

        return (item.Kind, item.ComponentType) switch
        {
            (ChangeKind.Added, ComponentType.Application) => Format("Event.DescAppInstalled", name, version),
            (ChangeKind.Removed, ComponentType.Application) => Format("Event.DescAppRemoved", name),
            (ChangeKind.Modified, ComponentType.Application) => IsVersionChange(item)
                ? Format("Event.DescAppUpdated", name, beforeVersion, version)
                : Format("Event.DescAppChanged", name),
            (ChangeKind.Added, ComponentType.Startup) => Format("Event.DescStartupAdded", name),
            (ChangeKind.Removed, ComponentType.Startup) => Format("Event.DescStartupRemoved", name),
            (ChangeKind.Modified, ComponentType.Startup) => Format("Event.DescStartupChanged", name),
            (ChangeKind.Added, ComponentType.Service) => Format("Event.DescServiceAdded", name),
            (ChangeKind.Removed, ComponentType.Service) => Format("Event.DescServiceRemoved", name),
            (ChangeKind.Modified, ComponentType.Service) => Format("Event.DescServiceChanged", name),
            (ChangeKind.Added, ComponentType.ScheduledTask) => Format("Event.DescTaskAdded", name),
            (ChangeKind.Removed, ComponentType.ScheduledTask) => Format("Event.DescTaskRemoved", name),
            (ChangeKind.Modified, ComponentType.ScheduledTask) => Format("Event.DescTaskChanged", name),
            (ChangeKind.Added, ComponentType.Folder) => Format("Event.DescFoldersAdded", count),
            (ChangeKind.Removed, ComponentType.Folder) => Format("Event.DescFoldersRemoved", count),
            (ChangeKind.Modified, ComponentType.Folder) => Format("Event.DescFoldersChanged", count),
            (ChangeKind.Added, ComponentType.File) => Format("Event.DescFilesAdded", count),
            (ChangeKind.Removed, ComponentType.File) => Format("Event.DescFilesRemoved", count),
            (ChangeKind.Modified, ComponentType.File) => Format("Event.DescFilesChanged", count),
            _ => Format("Event.DescChangeDetected", name)
        };
    }

    private static EventDetails BuildDetails(ChangeEvent item)
    {
        var fields = new List<FieldPair>(item.Details.Fields.Count);
        foreach (var field in item.Details.Fields)
        {
            fields.Add(new FieldPair
            {
                Label = FieldLabel(field.Label),
                Before = Value(field.Before),
                After = Value(field.After)
            });
        }

        var technical = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in item.Details.Technical)
        {
            technical[FieldLabel(pair.Key)] = Value(pair.Value) ?? string.Empty;
        }

        return new EventDetails
        {
            Fields = fields,
            Technical = technical,
            Explanation = Explanation(item.ComponentType),
            Effect = Effect(item),
            ComponentDetail = item.Details.ComponentDetail,
            ImportanceNote = item.Details.ImportanceNote,
            SamplePaths = item.Details.SamplePaths
        };
    }

    private static string Explanation(ComponentType type) => type switch
    {
        ComponentType.Application => Text("Event.ExplainApplication"),
        ComponentType.Startup => Text("Event.ExplainStartup"),
        ComponentType.Service => Text("Event.ExplainService"),
        ComponentType.ScheduledTask => Text("Event.ExplainTask"),
        ComponentType.File => Text("Event.ExplainFile"),
        ComponentType.Folder => Text("Event.ExplainFolder"),
        ComponentType.Registry => Text("Event.ExplainRegistry"),
        _ => Text("Event.ExplainOther")
    };

    private static string Effect(ChangeEvent item)
    {
        if (item.ComponentType == ComponentType.Application)
        {
            return item.Kind == ChangeKind.Removed
                ? Text("Event.EffectApplicationRemoved")
                : Text("Event.EffectApplication");
        }

        return item.ComponentType switch
        {
            ComponentType.Startup => Text(IsDisabled(item) ? "Event.EffectStartupDisabled" : "Event.EffectStartupEnabled"),
            ComponentType.Service => Text("Event.EffectService"),
            ComponentType.ScheduledTask => Text(IsDisabled(item) ? "Event.EffectTaskDisabled" : "Event.EffectTaskEnabled"),
            ComponentType.File => Text("Event.EffectFile"),
            ComponentType.Folder => Text("Event.EffectFolder"),
            ComponentType.Registry => Text("Event.EffectRegistry"),
            _ => Text("Event.EffectOther")
        };
    }

    private static string FieldLabel(string label) => label switch
    {
        "Версия" => Text("Event.FieldVersion"),
        "Издатель" => Text("Event.FieldPublisher"),
        "Папка установки" => Text("Event.FieldInstallFolder"),
        "Дата установки" => Text("Event.FieldInstallDate"),
        "Команда удаления" => Text("Event.FieldUninstallCommand"),
        "Команда запуска" => Text("Event.FieldStartupCommand"),
        "Состояние" => Text("Event.FieldState"),
        "Источник" => Text("Event.FieldSource"),
        "Способ запуска" => Text("Event.FieldStartMode"),
        "Путь к исполняемому файлу" => Text("Event.FieldExecutablePath"),
        "Отображаемое имя" => Text("Event.FieldDisplayName"),
        "Действия" => Text("Event.FieldActions"),
        "Триггеры" => Text("Event.FieldTriggers"),
        "Размер" => Text("Event.FieldSize"),
        "Изменён" => Text("Event.FieldModified"),
        "Расположение" => Text("Event.FieldLocation"),
        "Описание" => Text("Event.FieldDescription"),
        "Объектов" => Text("Event.FieldObjects"),
        "Тип компонента" => Text("Event.FieldComponentType"),
        "Идентификатор" => Text("Event.FieldIdentifier"),
        "Команда или путь" => Text("Event.FieldCommandOrPath"),
        "Подпись файла" => Text("Event.FieldFileSignature"),
        "Идентификатор программы" => Text("Event.FieldApplicationId"),
        "Ключ реестра" => Text("Event.FieldRegistryKey"),
        "Обнаружено объектов" => Text("Event.FieldObjectsFound"),
        "Папка" => Text("Event.FieldFolder"),
        "Исходное состояние сохранено" => Text("Event.FieldOriginalStateSaved"),
        "Имя службы" => Text("Event.FieldServiceName"),
        "Запуск с задержкой" => Text("Event.FieldDelayedStart"),
        "Имя файла" => Text("Event.FieldFileName"),
        "Цель ярлыка" => Text("Event.FieldShortcutTarget"),
        "Область" => Text("Event.FieldScope"),
        "Задание" => Text("Event.FieldTask"),
        "Папка задания" => Text("Event.FieldTaskFolder"),
        "Триггеров" => Text("Event.FieldTriggersCount"),
        "Автор" => Text("Event.FieldAuthor"),
        "Тип объекта" => Text("Event.FieldObjectType"),
        _ => label
    };

    private static string? Value(string? value) => value switch
    {
        null => null,
        "Включено" => Text("Event.ValueEnabled"),
        "Отключено" => Text("Event.ValueDisabled"),
        "Отключена" => Text("Event.ValueDisabled"),
        "Вручную" => Text("Event.ValueManual"),
        "Автоматически" => Text("Event.ValueAutomatic"),
        "Автоматически (с задержкой)" => Text("Event.ValueAutomaticDelayed"),
        "Работает" => Text("Event.ValueRunning"),
        "Остановлена" => Text("Event.ValueStopped"),
        "Да" => Text("Event.ValueYes"),
        "Нет" => Text("Event.ValueNo"),
        "не указан" => Text("Event.ValueNotSpecified"),
        "не указана" => Text("Event.ValueNotSpecified"),
        "не найдена" => Text("Event.ValueNotFound"),
        "не определена" => Text("Event.ValueNotDetermined"),
        "Системный источник" => Text("Event.ValueSystemSource"),
        "Файловая система" => Text("Event.ValueFileSystem"),
        "Список установленных программ" => Text("Event.ValueInstalledApps"),
        "Все пользователи" => Text("Event.ValueAllUsers"),
        "Текущий пользователь" => Text("Event.ValueCurrentUser"),
        "Папка" => Text("Event.ValueFolder"),
        "Файл" => Text("Event.ValueFile"),
        "Служба" => Text("Event.ValueService"),
        "Программа" => Text("Event.ValueApplication"),
        "Автозагрузка" => Text("Event.ValueStartup"),
        "Задание планировщика" => Text("Event.ValueScheduledTask"),
        "Реестр" => Text("Event.ValueRegistry"),
        "Компонент" => Text("Event.ValueComponent"),
        "Файлы" => Text("Event.ValueFiles"),
        "Папки" => Text("Event.ValueFolders"),
        "Да, с защитой Windows DPAPI" => Text("Event.ValueDpapiProtected"),
        "При входе" => Text("Event.ValueAtSignIn"),
        _ => value
    };

    private static bool IsVersionChange(ChangeEvent item)
    {
        var field = item.Details.Fields.FirstOrDefault(candidate => candidate.Label == "Версия");
        return field is not null && !string.Equals(field.Before, field.After, StringComparison.Ordinal);
    }

    private static bool IsEnabledTransition(ChangeEvent item)
    {
        var field = item.Details.Fields.FirstOrDefault(candidate => candidate.Label == "Состояние");
        return field is not null
               && !string.Equals(field.Before, field.After, StringComparison.Ordinal)
               && string.Equals(field.After, "Включено", StringComparison.Ordinal);
    }

    private static bool IsDisabled(ChangeEvent item)
    {
        var field = item.Details.Fields.FirstOrDefault(candidate => candidate.Label == "Состояние");
        return field is not null && string.Equals(field.After, "Отключено", StringComparison.Ordinal);
    }

    private static int Count(ChangeEvent item)
    {
        if (item.AffectedCount > 1)
        {
            return item.AffectedCount;
        }

        var field = item.Details.Fields.FirstOrDefault(candidate => candidate.Label == "Объектов");
        if (int.TryParse(field?.After, out var after) && after > 0)
        {
            return after;
        }

        if (int.TryParse(field?.Before, out var before) && before > 0)
        {
            return before;
        }

        return Math.Max(1, item.AffectedCount);
    }

    private static string Name(ChangeEvent item) =>
        string.IsNullOrWhiteSpace(item.ComponentName) ? item.AppName : item.ComponentName;

    private static string? FieldAfter(ChangeEvent item, string label) =>
        item.Details.Fields.FirstOrDefault(candidate => candidate.Label == label)?.After;

    private static string? FieldBefore(ChangeEvent item, string label) =>
        item.Details.Fields.FirstOrDefault(candidate => candidate.Label == label)?.Before;

    private static string Text(string key) => LocalizedStrings.Instance.Get(key);

    private static string Format(string key, params object?[] values) =>
        string.Format(LocalizedStrings.Instance.Culture, Text(key), values);
}
