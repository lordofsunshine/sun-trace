using SunTrace.Core.Detection;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Recovery;

public static class PauseLifecycleEventFactory
{
    public static ChangeEvent Create(PausedComponent item, bool paused, DateTimeOffset? detectedAt = null)
    {
        var kind = paused ? ChangeKind.Paused : ChangeKind.Restored;
        var title = paused ? "Компонент временно отключён" : "Компонент восстановлен";
        var description = paused
            ? $"«{item.DisplayName}» временно отключён через SunTrace. Исходное состояние сохранено и может быть восстановлено."
            : $"«{item.DisplayName}» возвращён в исходное состояние. SunTrace проверил результат.";

        return new ChangeEvent
        {
            Fingerprint = ChangeEventFactory.CreateFingerprint(item.ComponentId, "pause", kind, DateTime.UtcNow.Ticks.ToString()),
            AppId = item.AppId,
            AppName = item.AppName == "Связь не определена" ? string.Empty : item.AppName,
            Kind = kind,
            ComponentType = item.ComponentType,
            ComponentName = item.DisplayName,
            Title = title,
            Description = description,
            Details = new EventDetails
            {
                Fields =
                [
                    new FieldPair
                    {
                        Label = "Состояние",
                        Before = paused ? "Включено" : "Отключено",
                        After = paused ? "Отключено" : "Включено"
                    }
                ],
                Explanation = ComponentExplanation.ForType(item.ComponentType),
                Effect = ComponentExplanation.EffectForType(item.ComponentType, !paused),
                ComponentDetail = item.Detail,
                Technical = new Dictionary<string, string>
                {
                    ["Тип компонента"] = item.ComponentTypeLabel,
                    ["Идентификатор"] = item.ComponentId,
                    ["Исходное состояние сохранено"] = "Да, с защитой Windows DPAPI"
                }
            },
            Confidence = ConfidenceLevel.High,
            ConfidenceReasons = ["действие выполнено пользователем через SunTrace"],
            Importance = ImportanceLevel.Normal,
            Source = "Safe Pause",
            DetectedAt = detectedAt ?? DateTimeOffset.Now
        };
    }
}
