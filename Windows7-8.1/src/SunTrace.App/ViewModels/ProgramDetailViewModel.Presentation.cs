using SunTrace.App.Localization;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.App.ViewModels;

public partial class ProgramDetailViewModel
{
    private void BuildGroups(IReadOnlyList<ComponentSnapshot> components, HashSet<string> paused)
    {
        ComponentGroups.Clear();
        var order = new[]
        {
            ComponentType.Startup,
            ComponentType.ScheduledTask,
            ComponentType.Service,
            ComponentType.Registry,
            ComponentType.File,
            ComponentType.Folder,
            ComponentType.Other
        };

        var expanded = 0;
        foreach (var type in order)
        {
            var items = components.Where(c => c.Type == type).ToList();
            if (items.Count == 0)
            {
                continue;
            }

            var group = new ComponentGroupViewModel
            {
                Type = type,
                Title = TypeTitle(type),
                IsExpanded = expanded < 2
            };
            foreach (var component in items.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                group.Items.Add(new ComponentItemViewModel(component, paused.Contains(component.Id), PauseAsync));
            }

            ComponentGroups.Add(group);
            expanded++;
        }

        HasComponents = ComponentGroups.Count > 0;
        var total = components.Count;
        ComponentsLabel = total == 0
            ? LocalizedStrings.Instance["Ui.NoRelatedComponents"]
            : string.Format(LocalizedStrings.Instance["Ui.ComponentsCount"], total);
    }

    private static string TypeTitle(ComponentType type) => type switch
    {
        ComponentType.Startup => LocalizedStrings.Instance["Ui.GroupStartup"],
        ComponentType.Service => LocalizedStrings.Instance["Ui.GroupServices"],
        ComponentType.ScheduledTask => LocalizedStrings.Instance["Ui.GroupTasks"],
        ComponentType.Registry => LocalizedStrings.Instance["Ui.GroupRegistry"],
        ComponentType.File => LocalizedStrings.Instance["Ui.GroupFiles"],
        ComponentType.Folder => LocalizedStrings.Instance["Ui.GroupFolders"],
        _ => LocalizedStrings.Instance["Ui.GroupOther"]
    };

    private static string FormatDate(DateTimeOffset? value) =>
        value is { } time ? time.ToLocalTime().ToString("d MMMM yyyy", LocalizedStrings.Instance.Culture) : "-";
}
