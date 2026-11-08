using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SunTrace.App.Localization;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.App.ViewModels;

public sealed class ComponentGroupViewModel
{
    public ComponentType Type { get; init; }

    public string Title { get; init; } = string.Empty;

    public bool IsExpanded { get; set; }

    public ObservableCollection<ComponentItemViewModel> Items { get; } = new();

    public int Count => Items.Count;

    public string CountLabel => string.Format(LocalizedStrings.Instance["Ui.ComponentCountFormat"], Count);
}

public partial class ComponentItemViewModel : ObservableObject
{
    private readonly Func<ComponentItemViewModel, Task> _pause;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    [NotifyPropertyChangedFor(nameof(ShowProtectionNote))]
    private bool _isPaused;

    public ComponentItemViewModel(ComponentSnapshot component, bool isPaused, Func<ComponentItemViewModel, Task> pause)
    {
        Component = component;
        _pause = pause;
        _isPaused = isPaused;
        PauseCommand = new AsyncRelayCommand(() => _pause(this));
    }

    public ComponentSnapshot Component { get; }

    public IAsyncRelayCommand PauseCommand { get; }

    public string Name => Component.Name;

    public string Detail => string.IsNullOrWhiteSpace(Component.Detail) ? Component.Location ?? string.Empty : Component.Detail!;

    public ComponentType Type => Component.Type;

    public bool CanPause => Component.SupportsPause && Component.Pause is not null && !IsPaused;

    public bool ShowProtectionNote => !CanPause && !IsPaused && ProtectionNote.Length > 0;

    public string ProtectionNote => Component.Type switch
    {
        ComponentType.Service => LocalizedStrings.Instance["Ui.ReadOnly"],
        ComponentType.File or ComponentType.Folder => LocalizedStrings.Instance["Ui.ReadOnly"],
        ComponentType.Registry => LocalizedStrings.Instance["Ui.ReadOnly"],
        ComponentType.Application => LocalizedStrings.Instance["Ui.ReadOnly"],
        ComponentType.ScheduledTask when !Component.SupportsPause => LocalizedStrings.Instance["Ui.SystemTask"],
        ComponentType.Startup when !Component.SupportsPause => LocalizedStrings.Instance["Ui.ReadOnly"],
        _ => string.Empty
    };

    public string StateLabel
    {
        get
        {
            if (IsPaused)
            {
                return LocalizedStrings.Instance["Ui.PauseActive"];
            }

            return Component.Type switch
            {
                ComponentType.Service => Component.State ?? (Component.Enabled ? LocalizedStrings.Instance["Ui.Working"] : LocalizedStrings.Instance["Ui.Stopped"]),
                ComponentType.ScheduledTask => Component.Enabled ? LocalizedStrings.Instance["Ui.Active"] : LocalizedStrings.Instance["Ui.Disabled"],
                ComponentType.Startup => Component.Enabled ? LocalizedStrings.Instance["Ui.Active"] : LocalizedStrings.Instance["Ui.Disabled"],
                ComponentType.File or ComponentType.Folder => LocalizedStrings.Instance["Ui.Tracked"],
                _ => Component.Enabled ? LocalizedStrings.Instance["Ui.Active"] : LocalizedStrings.Instance["Ui.Disabled"]
            };
        }
    }
}
