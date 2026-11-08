using System.Windows;
using System.Windows.Controls;

namespace SunTrace.App.Views;

public partial class OverviewView : UserControl
{
    private int _statColumns;
    private bool _stackedBottom;

    public OverviewView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        Loaded += (_, _) => ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        var width = ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var columns = width >= 900 ? 4 : width >= 560 ? 2 : 1;
        if (columns != _statColumns)
        {
            StatsGrid.Columns = columns;
            _statColumns = columns;
        }

        var stackBottom = width < 940;
        if (stackBottom != _stackedBottom)
        {
            _stackedBottom = stackBottom;
            BottomSecondRow.Height = stackBottom ? GridLength.Auto : new GridLength(0);
            Grid.SetRow(SidePanel, stackBottom ? 1 : 0);
            Grid.SetColumn(SidePanel, stackBottom ? 0 : 2);
            Grid.SetColumnSpan(SidePanel, stackBottom ? 3 : 1);
            SidePanel.Margin = stackBottom ? new Thickness(0, 12, 0, 0) : new Thickness(0);
            BottomGap.Width = stackBottom ? new GridLength(0) : new GridLength(16);
            SideColumn.Width = stackBottom ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        }
    }
}
