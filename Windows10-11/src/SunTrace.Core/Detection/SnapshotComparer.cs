using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.Core.Detection;

public sealed class SnapshotComparer
{
    public SnapshotDiff Compare(SystemSnapshot previous, SystemSnapshot current)
    {
        var diff = new SnapshotDiff();
        CompareApps(previous, current, diff);
        CompareComponents(previous, current, diff);
        return diff;
    }

    private static void CompareApps(SystemSnapshot previous, SystemSnapshot current, SnapshotDiff diff)
    {
        var previousById = previous.Apps.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
        var currentById = current.Apps.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
        var matchedPrevious = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pair in currentById)
        {
            if (previousById.TryGetValue(pair.Key, out var before))
            {
                matchedPrevious.Add(pair.Key);
                if (HasAppChanged(before, pair.Value))
                {
                    diff.AppChanges.Add(new AppChange
                    {
                        Kind = ChangeKind.Modified,
                        AppId = pair.Value.Id,
                        Before = before,
                        After = pair.Value,
                        IsUpdate = !string.Equals(before.Version, pair.Value.Version, StringComparison.OrdinalIgnoreCase)
                                   && !string.IsNullOrWhiteSpace(pair.Value.Version),
                        Fields = BuildAppFields(before, pair.Value)
                    });
                }
            }
        }

        var unmatchedPrevious = previousById.Where(p => !matchedPrevious.Contains(p.Key)).Select(p => p.Value).ToList();
        var usedPrevious = new HashSet<string>(StringComparer.Ordinal);

        foreach (var app in currentById.Where(a => !previousById.ContainsKey(a.Key)).Select(a => a.Value))
        {
            var match = unmatchedPrevious.FirstOrDefault(p => !usedPrevious.Contains(p.Id) && AppIdentity.AreSameProduct(p, app));
            if (match is null)
            {
                diff.AppChanges.Add(new AppChange
                {
                    Kind = ChangeKind.Added,
                    AppId = app.Id,
                    After = app,
                    Fields = BuildAppFields(null, app)
                });
                continue;
            }

            usedPrevious.Add(match.Id);
            diff.AppChanges.Add(new AppChange
            {
                Kind = ChangeKind.Modified,
                AppId = app.Id,
                Before = match,
                After = app,
                IsUpdate = !string.Equals(match.Version, app.Version, StringComparison.OrdinalIgnoreCase)
                           && !string.IsNullOrWhiteSpace(app.Version),
                Fields = BuildAppFields(match, app)
            });
        }

        foreach (var before in unmatchedPrevious.Where(p => !usedPrevious.Contains(p.Id)))
        {
            diff.AppChanges.Add(new AppChange
            {
                Kind = ChangeKind.Removed,
                AppId = before.Id,
                Before = before,
                Fields = BuildAppFields(before, null)
            });
        }
    }

    private static bool HasAppChanged(InstalledApp before, InstalledApp after) =>
        !string.Equals(before.Version, after.Version, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(before.Publisher, after.Publisher, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(AppIdentity.NormalizedPath(before.InstallLocation), AppIdentity.NormalizedPath(after.InstallLocation), StringComparison.Ordinal)
        || before.InstallDate?.UtcDateTime.Date != after.InstallDate?.UtcDateTime.Date
        || !string.Equals(before.Name, after.Name, StringComparison.Ordinal)
        || !string.Equals(before.UninstallString, after.UninstallString, StringComparison.Ordinal);

    private static List<FieldPair> BuildAppFields(InstalledApp? before, InstalledApp? after)
    {
        var fields = new List<FieldPair>();
        Add(fields, "Версия", before?.Version, after?.Version);
        Add(fields, "Издатель", before?.Publisher, after?.Publisher);
        Add(fields, "Папка установки", before?.InstallLocation, after?.InstallLocation);
        Add(fields, "Дата установки", FormatOptionalDate(before?.InstallDate), FormatOptionalDate(after?.InstallDate));
        Add(fields, "Команда удаления", before?.UninstallString, after?.UninstallString);
        return fields;
    }

    private static string? FormatOptionalDate(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd.MM.yyyy");

    private static void Add(List<FieldPair> fields, string label, string? before, string? after)
    {
        var normalizedBefore = string.IsNullOrWhiteSpace(before) ? "-" : before.Trim();
        var normalizedAfter = string.IsNullOrWhiteSpace(after) ? "-" : after.Trim();
        if (!string.Equals(normalizedBefore, normalizedAfter, StringComparison.Ordinal))
        {
            fields.Add(new FieldPair { Label = label, Before = normalizedBefore, After = normalizedAfter });
        }
    }

    private static void CompareComponents(SystemSnapshot previous, SystemSnapshot current, SnapshotDiff diff)
    {
        var previousById = previous.Components.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        var currentById = current.Components.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());

        foreach (var pair in currentById)
        {
            if (previousById.TryGetValue(pair.Key, out var before))
            {
                var fields = BuildComponentFields(before, pair.Value);
                if (fields.Count > 0)
                {
                    diff.ComponentChanges.Add(new ComponentChange
                    {
                        Kind = ChangeKind.Modified,
                        Type = pair.Value.Type,
                        Key = pair.Key,
                        Before = before,
                        After = pair.Value,
                        Fields = fields
                    });
                }
            }
        }

        foreach (var pair in currentById.Where(p => !previousById.ContainsKey(p.Key)))
        {
            if (!CanDetectChange(current, pair.Value.Type))
            {
                continue;
            }

            diff.ComponentChanges.Add(new ComponentChange
            {
                Kind = ChangeKind.Added,
                Type = pair.Value.Type,
                Key = pair.Key,
                After = pair.Value,
                Fields = BuildComponentFields(null, pair.Value)
            });
        }

        foreach (var pair in previousById.Where(p => !currentById.ContainsKey(p.Key)))
        {
            if (!CanDetectChange(previous, pair.Value.Type) || !CanDetectChange(current, pair.Value.Type))
            {
                continue;
            }

            diff.ComponentChanges.Add(new ComponentChange
            {
                Kind = ChangeKind.Removed,
                Type = pair.Value.Type,
                Key = pair.Key,
                Before = pair.Value,
                Fields = BuildComponentFields(pair.Value, null)
            });
        }
    }

    private static bool CanDetectChange(SystemSnapshot snapshot, ComponentType type)
    {
        var section = type switch
        {
            ComponentType.Startup => ScanSection.Startup,
            ComponentType.Service => ScanSection.Services,
            ComponentType.ScheduledTask => ScanSection.ScheduledTasks,
            ComponentType.File or ComponentType.Folder => ScanSection.Files,
            _ => ScanSection.Applications
        };

        return snapshot.GetSection(section).Status == SectionStatus.Complete;
    }

    public static List<FieldPair> BuildComponentFields(ComponentSnapshot? before, ComponentSnapshot? after)
    {
        var fields = new List<FieldPair>();
        var type = after?.Type ?? before?.Type ?? ComponentType.Other;

        switch (type)
        {
            case ComponentType.Startup:
                Add(fields, "Команда запуска", before?.Detail, after?.Detail);
                Add(fields, "Состояние", StateLabel(before?.Enabled), StateLabel(after?.Enabled));
                Add(fields, "Источник", before?.Location, after?.Location);
                break;
            case ComponentType.Service:
                Add(fields, "Способ запуска", before?.StartMode, after?.StartMode);
                Add(fields, "Путь к исполняемому файлу", before?.Detail, after?.Detail);
                Add(fields, "Отображаемое имя", before?.Name, after?.Name);
                break;
            case ComponentType.ScheduledTask:
                Add(fields, "Действия", before?.Detail, after?.Detail);
                Add(fields, "Состояние", StateLabel(before?.Enabled), StateLabel(after?.Enabled));
                Add(fields, "Триггеры", before?.Details.GetValueOrDefault("Триггеры"), after?.Details.GetValueOrDefault("Триггеры"));
                break;
            case ComponentType.File:
            case ComponentType.Folder:
                Add(fields, "Размер", FormatSize(before?.SizeBytes), FormatSize(after?.SizeBytes));
                Add(fields, "Изменён", FormatOptionalDate(before?.ModifiedUtc), FormatOptionalDate(after?.ModifiedUtc));
                Add(fields, "Расположение", before?.Location, after?.Location);
                break;
            default:
                Add(fields, "Описание", before?.Detail, after?.Detail);
                break;
        }

        return fields;
    }

    public static string StateLabel(bool? enabled) => enabled switch
    {
        true => "Включено",
        false => "Отключено",
        _ => "-"
    };

    public static string FormatSize(long? bytes)
    {
        if (bytes is null or < 0)
        {
            return "-";
        }

        double value = bytes.Value;
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
