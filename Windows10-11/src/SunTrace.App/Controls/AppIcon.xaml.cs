using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using SunTrace.Core.Interfaces;

namespace SunTrace.App.Controls;

public partial class AppIcon : UserControl
{
    public static readonly DependencyProperty IconPathProperty = DependencyProperty.Register(
        nameof(IconPath),
        typeof(string),
        typeof(AppIcon),
        new PropertyMetadata(null, OnIconChanged));

    public static readonly DependencyProperty AppNameProperty = DependencyProperty.Register(
        nameof(AppName),
        typeof(string),
        typeof(AppIcon),
        new PropertyMetadata(string.Empty, OnNameChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size),
        typeof(double),
        typeof(AppIcon),
        new PropertyMetadata(40d, OnSizeChanged));

    private const int MaxCachedImages = 400;
    private static readonly ConcurrentDictionary<(string Path, int Size), BitmapSource> ImageCache = new();

    private CancellationTokenSource? _cancellation;
    private string? _loadedPath;
    private int _loadedSize;

    public AppIcon()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        UpdateLayoutMetrics();
    }

    public string? IconPath
    {
        get => (string?)GetValue(IconPathProperty);
        set => SetValue(IconPathProperty, value);
    }

    public string AppName
    {
        get => (string)GetValue(AppNameProperty);
        set => SetValue(AppNameProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public string Letter
    {
        get
        {
            var name = AppName?.Trim();
            return string.IsNullOrEmpty(name) ? "•" : name[..1].ToUpperInvariant();
        }
    }

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((AppIcon)d).RequestLoad();

    private static void OnNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (AppIcon)d;
        control.LetterText.GetBindingExpression(TextBlock.TextProperty)?.UpdateTarget();
    }

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((AppIcon)d).UpdateLayoutMetrics();

    private void UpdateLayoutMetrics()
    {
        var size = Size;
        Tile.CornerRadius = new CornerRadius(Math.Max(6, size * 0.24));
        LetterText.FontSize = Math.Max(12, size * 0.4);
        IconImage.Margin = new Thickness(size * 0.08);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => RequestLoad();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
    }

    private async void RequestLoad()
    {
        var path = IconPath;
        var size = (int)Math.Round(Size);
        if (string.IsNullOrWhiteSpace(path) || size <= 0)
        {
            ShowFallback();
            return;
        }

        if (_loadedPath == path && _loadedSize == size)
        {
            return;
        }

        if (ImageCache.TryGetValue((path, size), out var cachedImage))
        {
            _cancellation?.Cancel();
            ShowImage(cachedImage, path, size);
            return;
        }

        if (Directory.Exists(path))
        {
            ShowFallback();
            return;
        }

        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        var provider = App.Services?.GetService(typeof(IAppIconProvider)) as IAppIconProvider;
        if (provider is null)
        {
            ShowFallback();
            return;
        }

        try
        {
            var png = await provider.GetPngAsync(path, Math.Clamp(size * 2, 32, 128), token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (png is null)
            {
                ShowFallback();
                return;
            }

            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(png))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }

            bitmap.Freeze();
            if (ImageCache.Count >= MaxCachedImages)
            {
                ImageCache.Clear();
            }

            ImageCache[(path, size)] = bitmap;
            ShowImage(bitmap, path, size);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ShowFallback();
        }
    }

    private void ShowImage(BitmapSource image, string path, int size)
    {
        IconImage.Source = image;
        IconImage.Visibility = Visibility.Visible;
        Tile.Background = System.Windows.Media.Brushes.Transparent;
        Tile.BorderBrush = System.Windows.Media.Brushes.Transparent;
        _loadedPath = path;
        _loadedSize = size;
    }

    private void ShowFallback()
    {
        IconImage.Visibility = Visibility.Collapsed;
        IconImage.Source = null;
        Tile.SetResourceReference(Border.BackgroundProperty, "App.SurfaceAlt");
        Tile.SetResourceReference(Border.BorderBrushProperty, "App.Border");
        _loadedPath = null;
    }
}
