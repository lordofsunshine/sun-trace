using System.Windows;
using System.Windows.Controls;

namespace SunTrace.App.Controls;

public sealed class PlaceholderTextBox : TextBox
{
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder),
        typeof(string),
        typeof(PlaceholderTextBox),
        new PropertyMetadata(string.Empty));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }
}
