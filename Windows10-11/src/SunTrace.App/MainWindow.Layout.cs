using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SunTrace.App.Localization;
using Forms = System.Windows.Forms;

namespace SunTrace.App;

public partial class MainWindow
{
    private void Window_StateChanged(object? sender, EventArgs e) => UpdateMaximizeGlyph();
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout();

    private void ApplyResponsiveLayout()
    {
        if (HeaderSidebarColumn is null || SidebarColumn is null)
        {
            return;
        }

        var width = ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var compact = width < 1120;
        var narrow = width < 900;
        if (compact == _compactLayout && narrow == _narrowLayout)
        {
            return;
        }

        _compactLayout = compact;
        _narrowLayout = narrow;
        var sidebarWidth = compact ? 68 : 232;
        HeaderSidebarColumn.Width = new GridLength(sidebarWidth);
        SidebarColumn.Width = new GridLength(sidebarWidth);

        var labels = new[] { BrandText, NavOverviewText, NavProgramsText, NavHistoryText, NavPausedText, NavSettingsText, NavCaption };
        foreach (var label in labels)
        {
            label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }

        SearchHint.Visibility = width < 980 ? Visibility.Collapsed : Visibility.Visible;
        HeaderNextScan.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        HeaderScanButton.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;

        BrandPanel.Margin = compact ? new Thickness(20, 0, 0, 0) : new Thickness(18, 0, 0, 0);
        SearchHost.Margin = compact ? new Thickness(4, 0, 12, 0) : new Thickness(8, 0, 24, 0);
        SearchHost.MaxWidth = compact ? 380 : 500;
        PageHeader.Margin = compact ? new Thickness(16, 18, 16, 0) : new Thickness(24, 18, 24, 0);
        ScanProgressPanel.Margin = compact ? new Thickness(16, 10, 16, 0) : new Thickness(24, 10, 24, 0);
        PageHost.Margin = compact ? new Thickness(16, 14, 16, 18) : new Thickness(24, 16, 24, 20);
        ToastPanel.Width = Math.Min(350, Math.Max(280, width - 32));
    }

    private void ApplySavedBounds()
    {
        var settings = _viewModel.WindowSettings;
        var workArea = GetWorkArea(settings.WindowLeft, settings.WindowTop);
        var width = Math.Clamp(settings.WindowWidth, MinWidth, Math.Max(MinWidth, workArea.Width));
        var height = Math.Clamp(settings.WindowHeight, MinHeight, Math.Max(MinHeight, workArea.Height));
        Width = width;
        Height = height;
        if (!double.IsNaN(settings.WindowLeft) && !double.IsNaN(settings.WindowTop))
        {
            var visible = settings.WindowLeft < workArea.Right - 80
                          && settings.WindowLeft + 80 > workArea.Left
                          && settings.WindowTop < workArea.Bottom - 60
                          && settings.WindowTop + 60 > workArea.Top;
            if (visible)
            {
                Left = Math.Clamp(settings.WindowLeft, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
                Top = Math.Clamp(settings.WindowTop, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        if (settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private System.Windows.Rect GetWorkArea(double left, double top)
    {
        try
        {
            var point = double.IsNaN(left) || double.IsNaN(top)
                ? new System.Drawing.Point(0, 0)
                : new System.Drawing.Point((int)left, (int)top);
            var screen = Forms.Screen.FromPoint(point);
            var area = screen.WorkingArea;
            var dpi = VisualTreeHelper.GetDpi(this);
            return new System.Windows.Rect(
                area.Left / dpi.DpiScaleX,
                area.Top / dpi.DpiScaleY,
                area.Width / dpi.DpiScaleX,
                area.Height / dpi.DpiScaleY);
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }

    private Task SaveBoundsAsync()
    {
        var bounds = WindowState == WindowState.Maximized
            ? RestoreBounds
            : new Rect(Left, Top, ActualWidth, ActualHeight);
        return _viewModel.SaveWindowStateAsync(bounds.Width, bounds.Height, bounds.Left, bounds.Top, WindowState == WindowState.Maximized);
    }

    private void UpdateMaximizeGlyph()
    {
        if (MaximizeButton is null)
        {
            return;
        }

        MaximizeButton.Tag = FindResource(WindowState == WindowState.Maximized ? "Icon.Restore" : "Icon.Maximize");
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? LocalizedStrings.Instance["Ui.RestoreWindow"] : LocalizedStrings.Instance["Ui.MaximizeWindow"];
    }
}
