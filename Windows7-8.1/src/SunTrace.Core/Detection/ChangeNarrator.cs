using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.Core.Detection;

public static class ChangeNarrator
{
    public static (string Title, string Description) Describe(ComponentChange change, string? appName)
    {
        var name = change.DisplayName;
        var app = string.IsNullOrWhiteSpace(appName) ? null : appName;
        var type = change.Type;
        var before = change.Before;
        var after = change.After;
        var enabledBefore = before?.Enabled;
        var enabledAfter = after?.Enabled;

        return (change.Kind, type) switch
        {
            (ChangeKind.Added, ComponentType.Startup) => (
                "Новый элемент автозагрузки",
                app is null
                    ? $"Появился новый элемент автозагрузки «{name}». Он будет запускаться вместе с Windows."
                    : $"У программы «{app}» появился новый элемент автозагрузки «{name}». Теперь она сможет запускаться вместе с Windows."),

            (ChangeKind.Removed, ComponentType.Startup) => (
                "Элемент автозагрузки удалён",
                app is null
                    ? $"Элемент автозагрузки «{name}» больше не найден. Он не будет запускаться вместе с Windows."
                    : $"У программы «{app}» больше нет элемента автозагрузки «{name}»."),

            (ChangeKind.Modified, ComponentType.Startup) => (
                enabledBefore == false && enabledAfter == true ? "Элемент автозагрузки включён" : "Изменены параметры автозагрузки",
                enabledBefore == false && enabledAfter == true
                    ? $"Элемент «{name}» снова будет запускаться вместе с Windows."
                    : $"Изменены параметры запуска элемента автозагрузки «{name}»."),

            (ChangeKind.Added, ComponentType.Service) => (
                "Новая фоновая служба",
                app is null
                    ? $"Добавлена служба «{name}». Она может работать в фоне без открытого окна программы."
                    : $"Программа «{app}» добавила фоновую службу «{name}». Она может работать без открытого окна программы."),

            (ChangeKind.Removed, ComponentType.Service) => (
                "Служба удалена",
                $"Служба «{name}» больше не зарегистрирована в системе."),

            (ChangeKind.Modified, ComponentType.Service) => (
                "Изменены параметры службы",
                $"У службы «{name}» изменились параметры запуска или путь к исполняемому файлу."),

            (ChangeKind.Added, ComponentType.ScheduledTask) => (
                "Новое задание планировщика",
                app is null
                    ? $"Добавлено задание планировщика «{name}». Windows будет выполнять его автоматически."
                    : $"Программа «{app}» добавила задание планировщика «{name}». Windows будет выполнять его автоматически."),

            (ChangeKind.Removed, ComponentType.ScheduledTask) => (
                "Задание планировщика удалено",
                $"Задание «{name}» больше не зарегистрировано в планировщике Windows."),

            (ChangeKind.Modified, ComponentType.ScheduledTask) => (
                enabledBefore == false && enabledAfter == true ? "Задание планировщика включено" : "Изменены параметры задания",
                enabledBefore == false && enabledAfter == true
                    ? $"Задание «{name}» снова будет выполняться автоматически."
                    : $"Изменились действия, триггеры или состояние задания «{name}»."),

            (ChangeKind.Added, ComponentType.File or ComponentType.Folder) => (
                "Новые файлы программы",
                app is null
                    ? "На диске появились новые файлы или папки, связанные с программами."
                    : $"В папке программы «{app}» появились новые файлы или папки."),

            (ChangeKind.Removed, ComponentType.File or ComponentType.Folder) => (
                "Файлы программы удалены",
                app is null
                    ? "Часть файлов или папок программ больше не найдена."
                    : $"В папке программы «{app}» часть файлов или папок больше не найдена."),

            (ChangeKind.Modified, ComponentType.File or ComponentType.Folder) => (
                "Изменены файлы программы",
                app is null
                    ? "Изменились размер или дата изменения файлов программ."
                    : $"Изменились файлы в папке программы «{app}»."),

            _ => ("Обнаружено изменение", $"Обнаружено изменение компонента «{name}».")
        };
    }
}
