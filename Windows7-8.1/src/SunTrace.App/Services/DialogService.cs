using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SunTrace.App.ViewModels;
using SunTrace.App.Localization;
using SunTrace.App.Views.Dialogs;
using SunTrace.Core.Models;

namespace SunTrace.App.Services;

public sealed class DialogService : IDialogService
{
    private readonly IServiceProvider _services;

    public DialogService(IServiceProvider services)
    {
        _services = services;
    }

    private static Window? Owner => Application.Current?.MainWindow;

    public bool Confirm(
        string title,
        string message,
        string? confirmText = null,
        string? cancelText = null,
        bool danger = false,
        string? detail = null)
    {
        var window = new ConfirmWindow(new DialogOptions
        {
            Title = title,
            Message = message,
            Detail = detail,
            PrimaryText = confirmText ?? LocalizedStrings.Instance["Ui.Continue"],
            CancelText = cancelText ?? LocalizedStrings.Instance["Ui.Cancel"],
            Danger = danger
        })
        {
            Owner = Owner
        };

        return window.ShowDialog() == true;
    }

    public void Inform(string title, string message, string? detail = null)
    {
        var window = new ConfirmWindow(new DialogOptions
        {
            Title = title,
            Message = message,
            Detail = detail,
            PrimaryText = LocalizedStrings.Instance["Ui.Ok"]
        })
        {
            Owner = Owner
        };

        window.ShowDialog();
    }

    public void InformWithAction(string title, string message, string detail, string actionText, Action action)
    {
        var window = new ConfirmWindow(new DialogOptions
        {
            Title = title,
            Message = message,
            Detail = detail,
            PrimaryText = LocalizedStrings.Instance["Ui.Ok"],
            ActionText = actionText,
            Action = action
        })
        {
            Owner = Owner
        };

        window.ShowDialog();
    }

    public RestoreConflictChoice ResolveRestoreConflict(PausedComponent item, string message)
    {
        var window = new ConflictWindow(new ConflictDialogModel
        {
            Title = LocalizedStrings.Instance["Ui.RestoreConflict"],
            Message = message,
            ItemName = item.DisplayName,
            Detail = LocalizedStrings.Instance["Ui.ConflictDetail"]
        })
        {
            Owner = Owner
        };

        window.ShowDialog();
        return window.Choice;
    }

    public string? PickFile(string title, string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public void ShowTracker()
    {
        var viewModel = _services.GetRequiredService<TrackingDialogViewModel>();
        var window = new TrackerWindow(viewModel) { Owner = Owner };
        window.ShowDialog();
    }
}
