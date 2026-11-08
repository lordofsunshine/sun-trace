using SunTrace.Core.Enums;

namespace SunTrace.Core.Detection;

public static class ComponentExplanation
{
    public static string ForType(ComponentType type) => type switch
    {
        ComponentType.Startup => "Элемент автозагрузки. Windows запускает его автоматически при входе в систему.",
        ComponentType.Service => "Фоновая служба Windows. Она может работать без открытого окна программы.",
        ComponentType.ScheduledTask => "Задание планировщика Windows. Оно запускает программу или скрипт по расписанию либо событию.",
        ComponentType.File => "Файл, связанный с программой.",
        ComponentType.Folder => "Папка, связанная с программой.",
        ComponentType.Registry => "Параметр реестра, связанный с программой.",
        ComponentType.Application => "Запись об установленной программе в системном списке.",
        _ => "Назначение этого компонента SunTrace определить не может."
    };

    public static string EffectForType(ComponentType type, bool enabled) => type switch
    {
        ComponentType.Startup => enabled
            ? "Компонент может запускаться вместе с Windows."
            : "Компонент не будет запускаться вместе с Windows, пока его не включат снова.",
        ComponentType.Service => "Служба может работать в фоне без открытого окна программы.",
        ComponentType.ScheduledTask => enabled
            ? "Windows может выполнять задание автоматически."
            : "Задание отключено и не будет выполняться автоматически.",
        ComponentType.File or ComponentType.Folder => "Объект относится к файлам программы.",
        _ => "SunTrace не может однозначно определить влияние этого компонента."
    };
}
