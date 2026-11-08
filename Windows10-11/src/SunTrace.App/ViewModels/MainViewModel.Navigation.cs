using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SunTrace.App.Localization;

namespace SunTrace.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    public async Task NavigateAsync(string? section)
    {
        if (!Enum.TryParse<AppSection>(section, out var target))
        {
            return;
        }

        CurrentSection = target;
        IsSearchOpen = false;
        (PageTitle, PageSubtitle) = target switch
        {
            AppSection.Overview => (LocalizedStrings.Instance.Get("Shell.Overview"), LocalizedStrings.Instance.Get("Shell.OverviewSubtitle")),
            AppSection.Programs => (LocalizedStrings.Instance.Get("Shell.Programs"), LocalizedStrings.Instance.Get("Shell.ProgramsSubtitle")),
            AppSection.History => (LocalizedStrings.Instance.Get("Shell.History"), LocalizedStrings.Instance.Get("Shell.HistorySubtitle")),
            AppSection.Paused => (LocalizedStrings.Instance.Get("Shell.Paused"), LocalizedStrings.Instance.Get("Shell.PausedSubtitle")),
            _ => (LocalizedStrings.Instance.Get("Shell.Settings"), LocalizedStrings.Instance.Get("Shell.SettingsSubtitle"))
        };

        CurrentPage = target switch
        {
            AppSection.Overview => _services.GetRequiredService<OverviewViewModel>(),
            AppSection.Programs => _services.GetRequiredService<ProgramsViewModel>(),
            AppSection.History => _services.GetRequiredService<HistoryViewModel>(),
            AppSection.Paused => _services.GetRequiredService<PausedViewModel>(),
            _ => _services.GetRequiredService<SettingsViewModel>()
        };

        await RefreshCurrentAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task OpenAppAsync(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return;
        }

        var detail = _services.GetRequiredService<ProgramDetailViewModel>();
        await detail.LoadAsync(appId).ConfigureAwait(true);
        CurrentSection = AppSection.Programs;
        PageTitle = LocalizedStrings.Instance["Ui.AppCard"];
        PageSubtitle = detail.Title;
        CurrentPage = detail;
        IsSearchOpen = false;
    }

    public async Task ShowHistoryForAppAsync(string appId, string appName)
    {
        var history = _services.GetRequiredService<HistoryViewModel>();
        await history.ApplyAppFilterAsync(appId, appName).ConfigureAwait(true);
        await NavigateAsync(nameof(AppSection.History)).ConfigureAwait(true);
    }

    public async Task OpenProgramsAsync(string filter)
    {
        var programs = _services.GetRequiredService<ProgramsViewModel>();
        programs.SetFilter(filter);
        await NavigateAsync(nameof(AppSection.Programs)).ConfigureAwait(true);
    }

    private async Task RefreshCurrentAsync()
    {
        if (CurrentPage is IPageViewModel page)
        {
            await page.RefreshAsync().ConfigureAwait(true);
        }

        await RefreshShellCountsAsync().ConfigureAwait(true);
    }
}
