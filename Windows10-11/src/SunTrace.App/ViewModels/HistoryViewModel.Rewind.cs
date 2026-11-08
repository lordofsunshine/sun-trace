using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;
using SunTrace.App.Localization;

namespace SunTrace.App.ViewModels;

public partial class HistoryViewModel
{
    [RelayCommand]
    private async Task SetModeAsync(string? mode)
    {
        var rewind = mode == "Rewind";
        if (IsRewindMode == rewind)
        {
            return;
        }

        IsRewindMode = rewind;
        if (rewind)
        {
            await LoadSnapshotsAsync().ConfigureAwait(true);
        }
        else
        {
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task CompareAsync()
    {
        if (FromSnapshot is null || ToSnapshot is null)
        {
            return;
        }

        IsComparing = true;
        try
        {
            var from = await _store.GetSnapshotAsync(FromSnapshot.Id).ConfigureAwait(true);
            var to = await _store.GetSnapshotAsync(ToSnapshot.Id).ConfigureAwait(true);
            if (from is null || to is null)
            {
                TimelineChanges.Clear();
                HasTimelineChanges = false;
                RewindSummary = LocalizedStrings.Instance["Ui.RewindReadFailed"];
                return;
            }

            if (from.CapturedAt > to.CapturedAt)
            {
                (from, to) = (to, from);
            }

            var diff = _comparer.Compare(from, to);
            var items = new List<TimelineChangeItem>();

            foreach (var change in diff.AppChanges)
            {
                var version = change.After?.DisplayVersion ?? change.Before?.DisplayVersion;
                var detail = change.Kind switch
                {
                    ChangeKind.Added => string.Format(LocalizedStrings.Instance["Ui.AppearedFormat"], version),
                    ChangeKind.Removed => string.Format(LocalizedStrings.Instance["Ui.RemovedFormat"], version),
                    _ when change.IsUpdate => string.Format(LocalizedStrings.Instance["Ui.VersionChangeFormat"], change.Before?.DisplayVersion ?? "-", change.After?.DisplayVersion ?? "-"),
                    _ => LocalizedStrings.Instance["Ui.ProgramSettingsChanged"]
                };
                items.Add(new TimelineChangeItem
                {
                    AppName = change.AppName,
                    ChangeLabel = change.Kind switch
                    {
                        ChangeKind.Added => LocalizedStrings.Instance["Ui.AddedFeminine"],
                        ChangeKind.Removed => LocalizedStrings.Instance["Ui.RemovedFeminine"],
                        _ => change.IsUpdate ? LocalizedStrings.Instance["Ui.UpdatedFeminine"] : LocalizedStrings.Instance["Ui.ChangedFeminine"]
                    },
                    Detail = detail,
                    Kind = change.Kind,
                    Type = ComponentType.Application
                });
            }

            foreach (var change in diff.ComponentChanges)
            {
                var name = change.After?.Name ?? change.Before?.Name ?? LocalizedStrings.Instance["Ui.ComponentGeneric"];
                items.Add(new TimelineChangeItem
                {
                    AppName = name,
                    ChangeLabel = change.Kind switch
                    {
                        ChangeKind.Added => LocalizedStrings.Instance["Ui.DiffAdded"],
                        ChangeKind.Removed => LocalizedStrings.Instance["Ui.DiffRemoved"],
                        _ => LocalizedStrings.Instance["Ui.DiffChanged"]
                    },
                    Detail = change.After?.Detail ?? change.Before?.Detail ?? change.Type.ToString(),
                    Kind = change.Kind,
                    Type = change.Type
                });
            }

            IEnumerable<TimelineChangeItem> filtered = items;
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var term = SearchText.Trim();
                filtered = filtered.Where(i =>
                    i.AppName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || i.Detail.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            if (TypeFilter != "All")
            {
                filtered = filtered.Where(i => i.Type.ToString() == TypeFilter);
            }

            TimelineChanges.Clear();
            foreach (var item in filtered.OrderBy(i => i.Type).ThenBy(i => i.AppName))
            {
                TimelineChanges.Add(item);
            }

            HasTimelineChanges = TimelineChanges.Count > 0;
            RewindCounts = string.Format(
                LocalizedStrings.Instance["Ui.RewindCountsFormat"],
                diff.AppChanges.Count(a => a.Kind == ChangeKind.Added),
                diff.AppChanges.Count(a => a.IsUpdate),
                diff.ComponentChanges.Count);
            RewindSummary = $"{FormatSnapshot(from)} → {FormatSnapshot(to)}";
            RewindEmptyMessage = HasTimelineChanges
                ? string.Empty
                : LocalizedStrings.Instance["Ui.NoDifferences"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сравнить снимки.");
            RewindSummary = LocalizedStrings.Instance["Ui.CompareFailed"];
            HasTimelineChanges = false;
        }
        finally
        {
            IsComparing = false;
        }
    }

    partial void OnFromSnapshotChanged(SnapshotSummary? value) => TriggerCompare();
    partial void OnToSnapshotChanged(SnapshotSummary? value) => TriggerCompare();

    private void TriggerCompare()
    {
        if (!_suppressCompare && IsRewindMode && FromSnapshot is not null && ToSnapshot is not null)
        {
            _ = CompareAsync();
        }
    }

    private async Task LoadSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var snapshots = await _store.GetSnapshotsAsync(120, cancellationToken).ConfigureAwait(true);
            Snapshots.Clear();
            foreach (var item in snapshots)
            {
                Snapshots.Add(item);
            }

            HasSnapshots = Snapshots.Count > 0;
            if (!HasSnapshots)
            {
                TimelineChanges.Clear();
                HasTimelineChanges = false;
                RewindSummary = LocalizedStrings.Instance["Ui.NoSnapshotsInHistory"];
                RewindEmptyMessage = LocalizedStrings.Instance["Ui.RewindEmptyMessage"];
                return;
            }

            _suppressCompare = true;
            ToSnapshot = Snapshots[0];
            FromSnapshot = Snapshots.Count > 1 ? Snapshots[^1] : Snapshots[0];
            _suppressCompare = false;
            if (Snapshots.Count < 2)
            {
                RewindEmptyMessage = LocalizedStrings.Instance["Ui.NeedTwoSnapshots"];
            }

            await CompareAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось загрузить снимки для отмотки времени.");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string FormatSnapshot(SystemSnapshot snapshot) =>
        snapshot.CapturedAt.ToLocalTime().ToString("d MMMM yyyy, HH:mm", LocalizedStrings.Instance.Culture);
}
