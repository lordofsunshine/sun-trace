using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.Core.Detection;

public sealed class AppAssociationIndex
{
    private readonly ConfidenceEvaluator _evaluator = new();
    private readonly IReadOnlyList<InstalledApp> _apps;
    private readonly List<(string Location, string AppId)> _locations;

    public AppAssociationIndex(IReadOnlyList<InstalledApp> apps)
    {
        _apps = apps;
        _locations = apps
            .Where(a => PathHeuristics.IsUsableInstallLocation(a.InstallLocation))
            .Select(a => (Location: AppIdentity.NormalizedPath(a.InstallLocation)!, AppId: a.Id))
            .Where(pair => pair.Location.Length > 3)
            .OrderByDescending(pair => pair.Location.Length)
            .ToList();
    }

    public string? Resolve(ComponentSnapshot component)
    {
        if (component.Type is ComponentType.File or ComponentType.Folder)
        {
            var path = component.Detail ?? component.ExecutablePath;
            if (path is null)
            {
                return null;
            }

            var normalized = AppIdentity.NormalizedPath(path);
            if (normalized is null)
            {
                return null;
            }

            foreach (var (location, appId) in _locations)
            {
                if (normalized == location || normalized.StartsWith(location + "\\", StringComparison.Ordinal))
                {
                    return appId;
                }
            }

            return null;
        }

        var assessment = _evaluator.Evaluate(component, _apps);
        return assessment.App?.Id;
    }
}
