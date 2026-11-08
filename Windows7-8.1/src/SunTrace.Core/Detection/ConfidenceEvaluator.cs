using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.Core.Detection;

public sealed class ConfidenceAssessment
{
    public ConfidenceLevel Level { get; init; }

    public int Score { get; init; }

    public InstalledApp? App { get; init; }

    public List<string> Reasons { get; init; } = new();

    public string? AmbiguityNote { get; init; }

    public string Summary => Level switch
    {
        ConfidenceLevel.High => "Высокая достоверность связи",
        ConfidenceLevel.Medium => "Средняя достоверность связи",
        ConfidenceLevel.Low => "Низкая достоверность связи",
        _ => "Связь с программой не определена"
    };
}

public sealed class ConfidenceEvaluator
{
    public ConfidenceAssessment Evaluate(
        ComponentSnapshot component,
        IReadOnlyList<InstalledApp> apps,
        DateTimeOffset? detectedAt = null)
    {
        var candidates = new List<(InstalledApp App, int Score, List<string> Reasons)>();

        foreach (var app in apps)
        {
            var score = 0;
            var reasons = new List<string>();
            var installLocation = app.InstallLocation;

            if (PathHeuristics.IsUsableInstallLocation(installLocation))
            {
                var path = component.Type is ComponentType.File or ComponentType.Folder
                    ? component.Detail ?? component.ExecutablePath
                    : component.ExecutablePath ?? component.Detail;

                if (component.Type is ComponentType.File or ComponentType.Folder)
                {
                    path ??= component.Location is null ? null : Path.Combine(component.Location, component.Name);
                }

                if (PathHeuristics.IsUnder(path, installLocation))
                {
                    score += 75;
                    reasons.Add("файл или папка находится в папке установки программы");
                }
                else if (component.Location is not null && PathHeuristics.IsUnder(component.Location, installLocation))
                {
                    score += 55;
                    reasons.Add("расположение компонента совпадает с папкой установки");
                }
                else if (ContainsText(component.Detail, installLocation!) || ContainsText(component.ExecutablePath, installLocation!))
                {
                    score += 35;
                    reasons.Add("в параметрах компонента указана папка установки программы");
                }
            }

            if (AppIdentity.PublishersMatch(component.Publisher, app.Publisher)
                || AppIdentity.PublishersMatch(component.SignaturePublisher, app.Publisher)
                || AppIdentity.PublishersMatch(component.Publisher, app.SignaturePublisher))
            {
                score += 30;
                reasons.Add("совпадает издатель программы");
            }

            if (AppIdentity.NamesRelated(component.Name, app.Name))
            {
                score += 20;
                reasons.Add("название компонента совпадает с названием программы");
            }
            else if (AppIdentity.NamesRelated(component.Details.GetValueOrDefault("Имя файла"), app.Name)
                     || AppIdentity.NamesRelated(component.Details.GetValueOrDefault("Имя задания"), app.Name))
            {
                score += 15;
                reasons.Add("имя файла или задания связано с программой");
            }

            if (!string.IsNullOrWhiteSpace(app.MainExecutablePath)
                && string.Equals(AppIdentity.NormalizedPath(component.ExecutablePath), AppIdentity.NormalizedPath(app.MainExecutablePath), StringComparison.Ordinal))
            {
                score += 20;
                reasons.Add("это основной исполняемый файл программы");
            }

            if (detectedAt is not null && app.LastUpdated is not null
                && Math.Abs((detectedAt.Value - app.LastUpdated.Value).TotalMinutes) <= 45)
            {
                score += 8;
                reasons.Add("изменение обнаружено вскоре после изменения программы");
            }

            if (score > 0)
            {
                candidates.Add((app, Math.Min(score, 100), reasons));
            }
        }

        if (candidates.Count == 0)
        {
            return new ConfidenceAssessment
            {
                Level = ConfidenceLevel.Unknown,
                Score = 0,
                Reasons = ["недостаточно признаков для связи с программой"]
            };
        }

        var ordered = candidates.OrderByDescending(c => c.Score).ThenByDescending(c => c.Reasons.Count).ToList();
        var best = ordered[0];
        var level = best.Score switch
        {
            >= 70 => ConfidenceLevel.High,
            >= 40 => ConfidenceLevel.Medium,
            >= 15 => ConfidenceLevel.Low,
            _ => ConfidenceLevel.Unknown
        };

        string? ambiguity = null;
        if (ordered.Count > 1 && ordered[1].Score >= 15 && best.Score - ordered[1].Score < 15)
        {
            ambiguity = $"Похожие признаки есть у нескольких программ: {best.App.Name} и {ordered[1].App.Name}.";
            if (level is ConfidenceLevel.High or ConfidenceLevel.Medium)
            {
                level = level == ConfidenceLevel.High ? ConfidenceLevel.Medium : ConfidenceLevel.Low;
            }
        }

        if (level == ConfidenceLevel.Unknown)
        {
            return new ConfidenceAssessment
            {
                Level = ConfidenceLevel.Unknown,
                Score = best.Score,
                Reasons = ["недостаточно признаков для связи с программой"]
            };
        }

        return new ConfidenceAssessment
        {
            Level = level,
            Score = best.Score,
            App = best.App,
            Reasons = best.Reasons,
            AmbiguityNote = ambiguity
        };
    }

    private static bool ContainsText(string? text, string value) =>
        !string.IsNullOrWhiteSpace(text) && text.Contains(value, StringComparison.OrdinalIgnoreCase);
}
