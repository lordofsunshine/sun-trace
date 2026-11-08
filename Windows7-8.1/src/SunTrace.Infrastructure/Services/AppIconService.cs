using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SunTrace.Core.Interfaces;

namespace SunTrace.Infrastructure.Services;

public sealed class AppIconService : IAppIconProvider
{
    private const uint SIIGBFIconOnly = 0x00000004;
    private const uint SIIGBFResizeToFit = 0x00000000;
    private const uint SIIGBFBiggerSizeOk = 0x00000001;

    private readonly DataPaths _paths;
    private readonly ILogger<AppIconService> _logger;
    private readonly ConcurrentDictionary<string, byte[]?> _memoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _concurrency = new(4, 4);

    public AppIconService(DataPaths paths, ILogger<AppIconService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<byte[]?> GetPngAsync(string? executablePath, int size, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        var resolved = ResolveIconSource(executablePath);
        if (resolved is null)
        {
            return null;
        }

        var key = $"{resolved}|{size}";
        if (_memoryCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_memoryCache.TryGetValue(key, out cached))
            {
                return cached;
            }

            var diskPath = GetCachePath(resolved, size);
            if (File.Exists(diskPath))
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(diskPath, cancellationToken).ConfigureAwait(false);
                    _memoryCache[key] = bytes;
                    return bytes;
                }
                catch
                {
                }
            }

            var png = await Task.Run(() => ExtractPng(resolved, size), cancellationToken).ConfigureAwait(false);
            _memoryCache[key] = png;
            if (png is not null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(diskPath)!);
                    await File.WriteAllBytesAsync(diskPath, png, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            return png;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Не удалось извлечь иконку для {Path}.", executablePath);
            _memoryCache[key] = null;
            return null;
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private static string? ResolveIconSource(string path)
    {
        try
        {
            var value = path.Trim().Trim('"');
            if (value.Contains(','))
            {
                var comma = value.LastIndexOf(',');
                if (int.TryParse(value[(comma + 1)..], out _))
                {
                    value = value[..comma];
                }
            }

            value = Environment.ExpandEnvironmentVariables(value).Trim();
            if (File.Exists(value))
            {
                return value;
            }

            var directory = Path.GetDirectoryName(value);
            if (directory is not null && Directory.Exists(directory))
            {
                return directory;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private string GetCachePath(string source, int size)
    {
        var stamp = File.Exists(source) ? File.GetLastWriteTimeUtc(source).Ticks : 0;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{source}|{stamp}|{size}")))[..24].ToLowerInvariant();
        return Path.Combine(_paths.IconCacheDirectory, hash + ".png");
    }

    private static byte[]? ExtractPng(string source, int size)
    {
        var bitmap = TryCreateFromShellItem(source, Math.Max(16, Math.Min(256, size)));
        bitmap ??= TryCreateFromHIcon(source);
        if (bitmap is null)
        {
            return null;
        }

        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapSource? TryCreateFromShellItem(string path, int size)
    {
        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            if (factory is null)
            {
                return null;
            }

            var requested = new NativeSize { Width = size, Height = size };
            factory.GetImage(requested, SIIGBFIconOnly | SIIGBFResizeToFit | SIIGBFBiggerSizeOk, out hBitmap);
            if (hBitmap == IntPtr.Zero)
            {
                return null;
            }

            var bitmap = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(size, size));
            return bitmap;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }
        }
    }

    private static BitmapSource? TryCreateFromHIcon(string path)
    {
        var info = new ShFileInfo();
        try
        {
            var attributes = Directory.Exists(path) ? FileAttributeDirectory : FileAttributeNormal;
            var result = SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiLargeIcon | ShgfiUseFileAttributes);
            if (result == IntPtr.Zero || info.Icon == IntPtr.Zero)
            {
                return null;
            }

            var bitmap = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            return bitmap;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (info.Icon != IntPtr.Zero)
            {
                DestroyIcon(info.Icon);
            }
        }
    }

    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiUseFileAttributes = 0x000000010;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(NativeSize size, uint flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string path,
        IntPtr bindingContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory shellItem);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
