using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;
using SunTrace.App.Localization;
using SunTrace.App.Services;

namespace SunTrace.App.ViewModels;

public partial class HistoryViewModel
{
    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || IsLoading)
        {
            return;
        }

        var version = _loadVersion;
        try
        {
            IsLoading = true;
            var page = await _store.QueryEventsAsync(BuildQuery(Events.Count, 30)).ConfigureAwait(true);
            if (version != _loadVersion)
            {
                return;
            }

            foreach (var item in page.Items)
            {
                Events.Add(ChangeEventLocalizer.Localize(item));
            }

            TotalCount = page.TotalCount;
            HasMore = page.HasMore;
            UpdateLabels();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось загрузить продолжение истории.");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var page = await _store.QueryEventsAsync(BuildQuery(0, 30), cancellationToken).ConfigureAwait(true);
            if (version != _loadVersion)
            {
                return;
            }

            Events.Clear();
            foreach (var item in page.Items)
            {
                Events.Add(ChangeEventLocalizer.Localize(item));
            }

            TotalCount = page.TotalCount;
            HasMore = page.HasMore;
            HasEvents = TotalCount > 0;
            UpdateLabels();
            EmptyTitle = HasEvents ? string.Empty : LocalizedStrings.Instance["Ui.HistoryEmptyTitle"];
            EmptyMessage = HasEvents
                ? string.Empty
                : HasAppFilter
                    ? LocalizedStrings.Instance["Ui.NoEventsForProgram"]
                    : LocalizedStrings.Instance["Ui.HistoryEmptyMessageLong"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось загрузить историю изменений.");
            HasEvents = false;
            EmptyTitle = LocalizedStrings.Instance["Ui.HistoryReadFailed"];
            EmptyMessage = LocalizedStrings.Instance["Ui.HistoryReadFailedMessage"];
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private EventQuery BuildQuery(int offset, int pageSize)
    {
        var query = new EventQuery
        {
            SearchTerm = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            AppId = _appFilterId,
            Kind = KindFilter switch
            {
                "Added" => ChangeKind.Added,
                "Removed" => ChangeKind.Removed,
                "Modified" => ChangeKind.Modified,
                "Paused" => ChangeKind.Paused,
                "Restored" => ChangeKind.Restored,
                _ => null
            },
            ComponentType = TypeFilter switch
            {
                "Application" => ComponentType.Application,
                "Startup" => ComponentType.Startup,
                "Service" => ComponentType.Service,
                "ScheduledTask" => ComponentType.ScheduledTask,
                "File" => ComponentType.File,
                _ => null
            },
            Importance = ImportanceFilter switch
            {
                "Attention" => ImportanceLevel.Attention,
                "Check" => ImportanceLevel.Check,
                "Normal" => ImportanceLevel.Normal,
                _ => null
            },
            PageSize = pageSize,
            Offset = offset,
            From = Period switch
            {
                "7" => DateTimeOffset.Now.AddDays(-7),
                "30" => DateTimeOffset.Now.AddDays(-30),
                "90" => DateTimeOffset.Now.AddDays(-90),
                _ => null
            }
        };
        return query;
    }

    private void UpdateLabels()
    {
        TotalLabel = TotalCount == 0
            ? LocalizedStrings.Instance["Ui.NoEventsYet"]
            : string.Format(LocalizedStrings.Instance["Ui.ShownEventsFormat"], Events.Count, TotalCount);
        OnPropertyChanged(nameof(IsEventsMode));
    }
}
