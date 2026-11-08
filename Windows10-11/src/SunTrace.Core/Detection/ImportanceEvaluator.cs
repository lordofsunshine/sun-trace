using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.Core.Detection;

public static class ImportanceEvaluator
{
    public static (ImportanceLevel Level, string? Note) Evaluate(
        ComponentSnapshot? component,
        ComponentType type,
        ChangeKind kind,
        ConfidenceLevel confidence)
    {
        if (type == ComponentType.Application)
        {
            return (ImportanceLevel.Normal, null);
        }

        if (type is ComponentType.File or ComponentType.Folder)
        {
            return (ImportanceLevel.Normal, null);
        }

        var isEnabled = component?.Enabled ?? true;
        var path = component?.ExecutablePath ?? component?.Detail;
        var unusual = PathHeuristics.IsUnusualLocation(path);

        if (kind == ChangeKind.Added)
        {
            if (unusual)
            {
                return (ImportanceLevel.Attention,
                    "Компонент запускается из временной, пользовательской или загруженной папки. Это стоит проверить, но не является признаком вредоносной программы.");
            }

            if (type == ComponentType.Startup)
            {
                return (ImportanceLevel.Check, "Новый элемент будет запускаться вместе с Windows.");
            }

            if (type == ComponentType.ScheduledTask)
            {
                return (ImportanceLevel.Check, "Windows сможет выполнять новое задание автоматически.");
            }

            if (type == ComponentType.Service && !isEnabled)
            {
                return (ImportanceLevel.Normal, null);
            }

            return (ImportanceLevel.Check, "Появился новый фоновый компонент программы.");
        }

        if (kind == ChangeKind.Modified)
        {
            var wasEnabled = component?.Enabled ?? true;
            if (type is ComponentType.Startup or ComponentType.ScheduledTask)
            {
                return (ImportanceLevel.Check, "Изменилось поведение компонента при запуске системы.");
            }

            if (type == ComponentType.Service && confidence != ConfidenceLevel.High)
            {
                return (ImportanceLevel.Check, "Изменились параметры фоновой службы.");
            }
        }

        return (ImportanceLevel.Normal, null);
    }
}
