using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SunTrace.App.Services;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    public async Task OpenSearchResultAsync(SearchResult? result)
    {
        if (result is null)
        {
            return;
        }

        IsSearchOpen = false;
        switch (result.Kind)
        {
            case SearchResultKind.Application when !string.IsNullOrWhiteSpace(result.TargetId):
                await OpenAppAsync(result.TargetId).ConfigureAwait(true);
                break;
            case SearchResultKind.Event when !string.IsNullOrWhiteSpace(result.AppId):
                await ShowHistoryForAppAsync(result.AppId!, result.Subtitle).ConfigureAwait(true);
                break;
            case SearchResultKind.Component when !string.IsNullOrWhiteSpace(result.AppId):
                await OpenAppAsync(result.AppId).ConfigureAwait(true);
                break;
            default:
                await NavigateAsync(Enum.TryParse<AppSection>(result.TargetId, out var target)
                    ? target.ToString()
                    : nameof(AppSection.Overview)).ConfigureAwait(true);
                break;
        }
    }

    [RelayCommand]
    private void CloseSearch() => IsSearchOpen = false;

    partial void OnSearchTextChanged(string value)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;

        SearchResults.Clear();
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(IsSearchEmpty));

        if (string.IsNullOrWhiteSpace(value))
        {
            IsSearchOpen = false;
            IsSearching = false;
            return;
        }

        IsSearchOpen = true;
        _ = SearchAsync(value.Trim(), token);
    }

    private async Task SearchAsync(string term, CancellationToken cancellationToken)
    {
        try
        {
            IsSearching = true;
            OnPropertyChanged(nameof(IsSearchEmpty));
            await Task.Delay(190, cancellationToken).ConfigureAwait(true);
            var results = await _store.SearchAsync(term, 20, cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            SearchResults.Clear();
            foreach (var result in results)
            {
                SearchResults.Add(SearchResultLocalizer.Localize(result));
            }

            OnPropertyChanged(nameof(HasSearchResults));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка глобального поиска.");
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsSearching = false;
                OnPropertyChanged(nameof(IsSearchEmpty));
            }
        }
    }
}
