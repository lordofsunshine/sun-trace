using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.Core.Detection;

public static class AppStatusResolver
{
    public static AppStatus Resolve(
        DateTimeOffset now,
        DateTimeOffset? lastUpdateAt,
        DateTimeOffset? lastChangeAt,
        int attentionCount,
        int unreadCount,
        int pausedCount,
        bool isNew)
    {
        if (pausedCount > 0)
        {
            return AppStatus.PausedComponents;
        }

        if (attentionCount > 0)
        {
            return AppStatus.Attention;
        }

        if (unreadCount > 0)
        {
            return AppStatus.Changed;
        }

        return AppStatus.Unchanged;
    }

    public static string StatusLabel(AppStatus status) => status switch
    {
        AppStatus.Unchanged => "Без изменений",
        AppStatus.Updated => "Обновлено",
        AppStatus.Changed => "Есть изменения",
        AppStatus.Attention => "Требует проверки",
        AppStatus.PausedComponents => "Есть отключённые",
        AppStatus.New => "Недавно установлено",
        _ => "Без изменений"
    };
}
