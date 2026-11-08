using System.Windows.Controls;
using SunTrace.App.ViewModels;

namespace SunTrace.App.Views;

public partial class ProgramsView : UserControl
{
    public ProgramsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateColumns();
        DataContextChanged += (_, _) => UpdateColumns();
    }

    private static int ColumnsFor(double width) => width switch
    {
        >= 1080 => 4,
        >= 760 => 3,
        >= 500 => 2,
        _ => 1
    };

    private void UpdateColumns()
    {
        if (ActualWidth > 0 && DataContext is ProgramsViewModel model)
        {
            model.Columns = ColumnsFor(ActualWidth);
        }
    }
}
