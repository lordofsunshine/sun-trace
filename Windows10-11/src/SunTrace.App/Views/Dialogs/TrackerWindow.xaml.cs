using System.Windows;
using System.Windows.Input;
using SunTrace.App.ViewModels;

namespace SunTrace.App.Views.Dialogs;

public partial class TrackerWindow : Window
{
    private readonly TrackingDialogViewModel _viewModel;

    public TrackerWindow(TrackingDialogViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        viewModel.CloseRequested += OnCloseRequested;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.CancelImmediately();
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
