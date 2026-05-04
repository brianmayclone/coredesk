using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CoreDesk.Windows.AppDiscovery;

internal sealed class WindowsIconCache
{
    private readonly string _cacheDirectory;

    public WindowsIconCache()
    {
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoreDesk",
            "IconCache");
        Directory.CreateDirectory(_cacheDirectory);
    }

    public string? GetOrCreateIconPath(string id, string? executablePath, string? shortcutPath)
    {
        var cachePath = Path.Combine(_cacheDirectory, $"{SafeFileName(id)}-v3.png");
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        var source = ResolveIconSource(executablePath, shortcutPath);
        if (source is null)
        {
            return null;
        }

        try
        {
            using var bitmap = TryCreateHighResolutionIconBitmap(source) ?? TryCreateAssociatedIconBitmap(source);
            if (bitmap is null)
            {
                return null;
            }

            using var trimmed = TrimTransparentPadding(bitmap);
            trimmed.Save(cachePath, ImageFormat.Png);
            return cachePath;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> GetOrCreateStoreIconPathAsync(string id, global::Windows.Storage.Streams.IRandomAccessStreamReference? logo)
    {
        if (logo is null)
        {
            return null;
        }

        var cachePath = Path.Combine(_cacheDirectory, $"{SafeFileName(id)}-store-v3.png");
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        try
        {
            using var stream = await logo.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > int.MaxValue)
            {
                return null;
            }

            var buffer = new global::Windows.Storage.Streams.Buffer((uint)stream.Size);
            await stream.ReadAsync(buffer, buffer.Capacity, global::Windows.Storage.Streams.InputStreamOptions.None);
            global::Windows.Security.Cryptography.CryptographicBuffer.CopyToByteArray(buffer, out var bytes);

            using var input = new MemoryStream(bytes);
            using var bitmap = new Bitmap(input);
            using var trimmed = TrimTransparentPadding(bitmap);
            trimmed.Save(cachePath, ImageFormat.Png);
            return cachePath;
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap TrimTransparentPadding(Bitmap source)
    {
        var bounds = FindContentBounds(source);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return new Bitmap(source);
        }

        return source.Clone(bounds, PixelFormat.Format32bppArgb);
    }

    private static Rectangle FindContentBounds(Bitmap source)
    {
        var left = source.Width;
        var top = source.Height;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                if (source.GetPixel(x, y).A < 16)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        return right < left || bottom < top
            ? Rectangle.Empty
            : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private static string? ResolveIconSource(string? executablePath, string? shortcutPath)
    {
        if (!string.IsNullOrWhiteSpace(shortcutPath) && File.Exists(shortcutPath))
        {
            var resolved = WindowsShortcutResolver.TryResolve(shortcutPath);
            if (File.Exists(resolved.IconPath))
            {
                return resolved.IconPath;
            }

            if (File.Exists(resolved.TargetPath))
            {
                return resolved.TargetPath;
            }

            return shortcutPath;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        if (File.Exists(executablePath))
        {
            return executablePath;
        }

        return ResolveExecutableFromPath(executablePath);
    }

    private static string? ResolveExecutableFromPath(string executable)
    {
        try
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(directory.Trim(), executable);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static Bitmap? TryCreateHighResolutionIconBitmap(string source)
    {
        nint largeIcon = 0;
        nint smallIcon = 0;
        try
        {
            var result = SHDefExtractIcon(source, 0, 0, out largeIcon, out smallIcon, MakeIconSize(256, 64));
            if (result < 0 || largeIcon == 0)
            {
                return null;
            }

            using var icon = (Icon)Icon.FromHandle(largeIcon).Clone();
            return icon.ToBitmap();
        }
        catch
        {
            return null;
        }
        finally
        {
            if (largeIcon != 0)
            {
                _ = DestroyIcon(largeIcon);
            }

            if (smallIcon != 0)
            {
                _ = DestroyIcon(smallIcon);
            }
        }
    }

    private static Bitmap? TryCreateAssociatedIconBitmap(string source)
    {
        using var icon = Icon.ExtractAssociatedIcon(source);
        return icon?.ToBitmap();
    }

    private static uint MakeIconSize(int large, int small) => (uint)((small << 16) | (large & 0xFFFF));

    private static string SafeFileName(string value)
    {
        return string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string iconFile, int iconIndex, uint flags, out nint largeIcon, out nint smallIcon, uint iconSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint icon);
}
