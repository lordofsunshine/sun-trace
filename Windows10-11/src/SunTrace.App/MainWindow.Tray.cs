using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SunTrace.App.ViewModels;
#if !LEGACY_WINDOWS
using Microsoft.Toolkit.Uwp.Notifications;
#endif
using SunTrace.App.Localization;
using Forms = System.Windows.Forms;

namespace SunTrace.App;

public partial class MainWindow
{
    public void ActivateFromExternal() => ShowFromTray();

    private Forms.NotifyIcon BuildTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(LocalizedStrings.Instance["Ui.TrayOpen"], null, (_, _) => ShowFromTray());
        menu.Items.Add(LocalizedStrings.Instance["Ui.TrayCheck"], null, async (_, _) => await _viewModel.ScanNowAsync());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(LocalizedStrings.Instance["Ui.TrayExit"], null, (_, _) => ExitApplication());

        var icon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = LocalizedStrings.Instance["Ui.TrayTooltip"],
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => ShowFromTray();
        return icon;
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/SunTrace.ico"));
            if (resource?.Stream is { } stream)
            {
                using (stream)
                {
                    return new System.Drawing.Icon(stream);
                }
            }
        }
        catch
        {
        }

        return System.Drawing.SystemIcons.Application;
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private async void ExitApplication()
    {
        if (_allowClose)
        {
            return;
        }

        _allowClose = true;
        _tray.Visible = false;
        await Task.WhenAny(SaveBoundsAsync(), Task.Delay(2000));
        App.RequestShutdown();
    }

    private void OnNotificationRequested(object? sender, NotificationEventArgs e)
    {
        if (!_viewModel.WindowSettings.NotificationsEnabled)
        {
            return;
        }

#if LEGACY_WINDOWS
        _tray.ShowBalloonTip(5000, e.Title, e.Message, Forms.ToolTipIcon.Info);
#else
        try
        {
            var content = new ToastContentBuilder()
                .AddText(e.Title)
                .AddText(e.Message);
            if (!string.IsNullOrWhiteSpace(e.AppId))
            {
                content.AddArgument("appId", e.AppId);
            }

            content.Show();
        }
        catch
        {
            _tray.ShowBalloonTip(5000, e.Title, e.Message, Forms.ToolTipIcon.Info);
        }
#endif
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _tray.Text = LocalizedStrings.Instance["Ui.TrayTooltip"];
        if (_trayMenu.Items.Count >= 4)
        {
            _trayMenu.Items[0].Text = LocalizedStrings.Instance["Ui.TrayOpen"];
            _trayMenu.Items[1].Text = LocalizedStrings.Instance["Ui.TrayCheck"];
            _trayMenu.Items[3].Text = LocalizedStrings.Instance["Ui.TrayExit"];
        }
    }

    public async Task OpenFromNotificationAsync(string? appId)
    {
        ShowFromTray();
        if (!string.IsNullOrWhiteSpace(appId))
        {
            await _viewModel.OpenAppAsync(appId);
            return;
        }

        await _viewModel.NavigateAsync(nameof(AppSection.Programs));
    }
}
