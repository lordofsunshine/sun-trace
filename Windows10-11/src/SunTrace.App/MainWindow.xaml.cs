using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SunTrace.App.ViewModels;
using SunTrace.App.Localization;
using SunTrace.Infrastructure.Windows;
using Forms = System.Windows.Forms;

namespace SunTrace.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ContextMenuStrip _trayMenu;
    private bool _closing;
    private bool _allowClose;
    private bool _backgroundNoticeShown;
    private bool _compactLayout;
    private bool _narrowLayout;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _tray = BuildTrayIcon();
        _trayMenu = _tray.ContextMenuStrip!;
        viewModel.NotificationRequested += OnNotificationRequested;
        LocalizedStrings.Instance.LanguageChanged += OnLanguageChanged;
        IsVisibleChanged += (_, _) => ProcessThrottle.SetBackground(!IsVisible);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySavedBounds();
        ApplyResponsiveLayout();
        UpdateMaximizeGlyph();
        if (App.StartMinimized)
        {
            Hide();
        }

        SearchBox.Focus();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || App.IsShuttingDown)
        {
            return;
        }

        e.Cancel = true;
        if (_closing)
        {
            return;
        }

        _closing = true;
        Hide();
        if (!_backgroundNoticeShown)
        {
            _backgroundNoticeShown = true;
            _tray.ShowBalloonTip(4000, "SunTrace", LocalizedStrings.Instance["Ui.BackgroundModeNotice"], Forms.ToolTipIcon.Info);
        }

        await Task.WhenAny(SaveBoundsAsync(), Task.Delay(2000));
        _closing = false;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        LocalizedStrings.Instance.LanguageChanged -= OnLanguageChanged;
        _tray.Dispose();
    }

    public void PrepareForShutdown()
    {
        _allowClose = true;
        _tray.Visible = false;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _viewModel.IsSearchOpen)
        {
            _viewModel.CloseSearchCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        var section = e.Key switch
        {
            Key.D1 or Key.NumPad1 => "Overview",
            Key.D2 or Key.NumPad2 => "Programs",
            Key.D3 or Key.NumPad3 => "History",
            Key.D4 or Key.NumPad4 => "Paused",
            Key.D5 or Key.NumPad5 => "Settings",
            _ => null
        };

        if (section is not null)
        {
            _viewModel.NavigateCommand.Execute(section);
            e.Handled = true;
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
