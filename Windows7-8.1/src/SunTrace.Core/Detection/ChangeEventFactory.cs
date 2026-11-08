using System.Security.Cryptography;
using System.Text;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.Core.Detection;

public sealed class ChangeEventFactory
{
    private readonly ConfidenceEvaluator _confidenceEvaluator = new();

    public List<ChangeEvent> Build(
        SnapshotDiff diff,
        SystemSnapshot previous,
        SystemSnapshot current,
        long snapshotId,
        DateTimeOffset detectedAt,
        long? sessionId = null)
    {
        var events = new List<ChangeEvent>();
        var appsForAdded = current.Apps;
        var appsForRemoved = previous.Apps;

        foreach (var change in diff.AppChanges)
        {
            events.Add(CreateAppEvent(change, snapshotId, detectedAt, sessionId));
        }

        foreach (var change in diff.ComponentChanges.Where(c => c.Type is not (ComponentType.File or ComponentType.Folder)))
        {
            var apps = change.Kind == ChangeKind.Removed ? appsForRemoved : appsForAdded;
            events.Add(CreateComponentEvent(change, apps, snapshotId, detectedAt, sessionId));
        }

        events.AddRange(CreateFileEvents(diff, previous, current, snapshotId, detectedAt, sessionId));
        return events;
    }

    private static ChangeEvent CreateAppEvent(AppChange change, long snapshotId, DateTimeOffset detectedAt, long? sessionId)
    {
        var app = change.After ?? change.Before!;
        var (title, description) = change.Kind switch
        {
            ChangeKind.Added => ("Программа установлена",
                $"«{app.Name}» появилась на компьютере. Версия: {app.DisplayVersion}."),
            ChangeKind.Removed => ("Программа удалена",
                $"«{app.Name}» больше не найдена в списке установленных программ."),
            _ when change.IsUpdate => ("Обновление программы",
                $"«{app.Name}» обновлена: версия {change.Before?.DisplayVersion} → {app.DisplayVersion}."),
            _ => ("Изменены параметры программы",
                $"У программы «{app.Name}» изменились сведения в системном списке установленных программ.")
        };

        var details = new EventDetails
        {
            Fields = change.Fields,
            Explanation = ComponentExplanation.ForType(ComponentType.Application),
            Effect = change.Kind == ChangeKind.Removed
                ? "Запись об установке удалена. Возможно, программа была удалена или её установщик обновил запись."
                : "Windows будет считать эту версию программы установленной.",
            ComponentDetail = app.InstallLocation,
            Technical = new Dictionary<string, string>
            {
                ["Идентификатор программы"] = app.Id,
                ["Ключ реестра"] = app.RegistryKey ?? "-",
                ["Команда удаления"] = app.UninstallString ?? "-"
            }
        };

        return new ChangeEvent
        {
            Fingerprint = CreateFingerprint(app.Id, "app", change.Kind, app.Version ?? string.Empty),
            AppId = change.Kind == ChangeKind.Removed ? null : app.Id,
            AppName = app.Name,
            Kind = change.Kind,
            ComponentType = ComponentType.Application,
            ComponentName = app.Name,
            Title = title,
            Description = description,
            Details = details,
            Confidence = ConfidenceLevel.High,
            ConfidenceReasons = ["программа найдена в системном списке установленных программ"],
            Importance = ImportanceLevel.Normal,
            Source = "Список установленных программ",
            DetectedAt = detectedAt,
            SnapshotId = snapshotId,
            SessionId = sessionId
        };
    }

    private ChangeEvent CreateComponentEvent(
        ComponentChange change,
        IReadOnlyList<InstalledApp> apps,
        long snapshotId,
        DateTimeOffset detectedAt,
        long? sessionId)
    {
        var component = change.After ?? change.Before!;
        var assessment = _confidenceEvaluator.Evaluate(component, apps, detectedAt);
        var appName = assessment.App?.Name;
        var (title, description) = ChangeNarrator.Describe(change, appName);
        var (importance, importanceNote) = ImportanceEvaluator.Evaluate(component, change.Type, change.Kind, assessment.Level);
        var reasons = new List<string>(assessment.Reasons);
        if (assessment.AmbiguityNote is not null)
        {
            reasons.Add(assessment.AmbiguityNote);
        }

        var details = new EventDetails
        {
            Fields = change.Fields,
            Explanation = ComponentExplanation.ForType(change.Type),
            Effect = ComponentExplanation.EffectForType(change.Type, component.Enabled),
            ComponentDetail = component.Detail,
            ImportanceNote = importanceNote,
            Technical = new Dictionary<string, string>
            {
                ["Тип компонента"] = component.TypeLabel,
                ["Идентификатор"] = component.Id,
                ["Расположение"] = component.Location ?? "-",
                ["Команда или путь"] = component.Detail ?? component.ExecutablePath ?? "-",
                ["Издатель"] = component.Publisher ?? "не указан",
                ["Подпись файла"] = component.SignaturePublisher ?? "не найдена"
            }
        };

        var stateSignature = $"{component.Detail}|{component.Enabled}|{component.StartMode}|{component.SizeBytes}|{component.ModifiedUtc:O}";
        return new ChangeEvent
        {
            Fingerprint = CreateFingerprint(component.Id, change.Type.ToString(), change.Kind, stateSignature),
            AppId = assessment.App?.Id,
            AppName = appName ?? string.Empty,
            Kind = change.Kind,
            ComponentType = change.Type,
            ComponentName = component.Name,
            Title = title,
            Description = description,
            Details = details,
            Confidence = assessment.Level,
            ConfidenceReasons = reasons,
            Importance = importance,
            Source = component.Owner ?? "Системный источник",
            DetectedAt = detectedAt,
            SnapshotId = snapshotId,
            SessionId = sessionId
        };
    }

    private List<ChangeEvent> CreateFileEvents(
        SnapshotDiff diff,
        SystemSnapshot previous,
        SystemSnapshot current,
        long snapshotId,
        DateTimeOffset detectedAt,
        long? sessionId)
    {
        var result = new List<ChangeEvent>();
        var fileChanges = diff.ComponentChanges
            .Where(c => c.Type is ComponentType.File or ComponentType.Folder)
            .ToList();
        if (fileChanges.Count == 0)
        {
            return result;
        }

        var grouped = fileChanges
            .GroupBy(change =>
            {
                var component = change.After ?? change.Before!;
                var assessment = _confidenceEvaluator.Evaluate(component, change.Kind == ChangeKind.Removed ? previous.Apps : current.Apps, detectedAt);
                return new
                {
                    AppId = assessment.App?.Id,
                    AppName = assessment.App?.Name,
                    Kind = change.Kind,
                    Section = TopLevelFolder(component.Detail ?? component.Location)
                };
            })
            .Take(60)
            .ToList();

        foreach (var group in grouped)
        {
            var first = group.First();
            var sample = group.Select(c => c.After ?? c.Before!).ToList();
            var componentType = sample.All(c => c.Type == ComponentType.Folder) ? ComponentType.Folder : ComponentType.File;
            var title = group.Key.Kind switch
            {
                ChangeKind.Added => componentType == ComponentType.Folder ? "Новые папки программы" : "Новые файлы программы",
                ChangeKind.Removed => "Файлы программы удалены",
                _ => "Изменены файлы программы"
            };
            var description = group.Key.Kind switch
            {
                ChangeKind.Added => group.Key.AppName is null
                    ? $"Обнаружено {group.Count()} новых объектов в системных папках программ."
                    : $"В папке программы «{group.Key.AppName}» обнаружено {group.Count()} новых объектов.",
                ChangeKind.Removed => group.Key.AppName is null
                    ? $"Не найдено {group.Count()} объектов, которые были раньше."
                    : $"В папке программы «{group.Key.AppName}» не найдено {group.Count()} объектов.",
                _ => group.Key.AppName is null
                    ? $"Изменились размер или дата изменения у {group.Count()} объектов."
                    : $"У {group.Count()} объектов в папке программы «{group.Key.AppName}» изменились размер или дата изменения."
            };

            var paths = sample.Select(c => c.Detail ?? c.Location ?? c.Name).Where(p => !string.IsNullOrWhiteSpace(p)).Take(20).ToList();
            var importance = group.Key.Kind == ChangeKind.Added ? ImportanceLevel.Normal : ImportanceLevel.Normal;
            result.Add(new ChangeEvent
            {
                Fingerprint = CreateFingerprint(group.Key.AppId ?? group.Key.Section ?? "files", "files-" + componentType, group.Key.Kind, string.Join(';', paths)),
                AppId = group.Key.AppId,
                AppName = group.Key.AppName ?? string.Empty,
                Kind = group.Key.Kind,
                ComponentType = componentType,
                ComponentName = group.Key.Section ?? "Файлы программы",
                Title = title,
                Description = description,
                Details = new EventDetails
                {
                    Fields = [new FieldPair { Label = "Объектов", Before = group.Key.Kind == ChangeKind.Added ? "0" : group.Count().ToString(), After = group.Key.Kind == ChangeKind.Removed ? "0" : group.Count().ToString() }],
                    Explanation = ComponentExplanation.ForType(componentType),
                    Effect = ComponentExplanation.EffectForType(componentType, true),
                    ImportanceNote = null,
                    ComponentDetail = group.Key.Section,
                    SamplePaths = paths,
                    Technical = new Dictionary<string, string>
                    {
                        ["Тип компонента"] = componentType == ComponentType.Folder ? "Папка" : "Файл",
                        ["Обнаружено объектов"] = group.Count().ToString(),
                        ["Папка"] = group.Key.Section ?? "-"
                    }
                },
                Confidence = group.Key.AppId is null ? ConfidenceLevel.Unknown : ConfidenceLevel.High,
                ConfidenceReasons = group.Key.AppId is null
                    ? ["файлы находятся вне известных папок установки"]
                    : ["файлы находятся в папке установки программы"],
                Importance = importance,
                Source = "Файловая система",
                DetectedAt = detectedAt,
                SnapshotId = snapshotId,
                SessionId = sessionId,
                AffectedCount = group.Count()
            });
        }

        return result;
    }

    private static string? TopLevelFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.TrimEnd('\\');
        var parent = Path.GetDirectoryName(trimmed);
        return parent ?? trimmed;
    }

    public static string CreateFingerprint(string scope, string type, ChangeKind kind, string state)
    {
        var raw = $"{scope}|{type}|{kind}|{state}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..32].ToLowerInvariant();
    }
}
