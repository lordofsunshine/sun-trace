using System.Windows;
using System.Windows.Input;
using SunTrace.App.Services;

namespace SunTrace.App.Views.Dialogs;

public partial class ConflictWindow : Window
{
    public ConflictWindow(ConflictDialogModel model)
    {
        InitializeComponent();
        DataContext = model;
    }

    public RestoreConflictChoice Choice { get; private set; } = RestoreConflictChoice.Cancel;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = RestoreConflictChoice.Cancel;
        DialogResult = false;
    }

    private void Keep_Click(object sender, RoutedEventArgs e)
    {
        Choice = RestoreConflictChoice.KeepCurrent;
        DialogResult = true;
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        Choice = RestoreConflictChoice.Retry;
        DialogResult = true;
    }
}
