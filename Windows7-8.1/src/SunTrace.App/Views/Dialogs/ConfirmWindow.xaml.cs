using System.Windows;
using System.Windows.Input;
using SunTrace.App.Services;

namespace SunTrace.App.Views.Dialogs;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow(DialogOptions options)
    {
        InitializeComponent();
        DataContext = options;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Primary_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DialogOptions { Action: { } action })
        {
            action();
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
