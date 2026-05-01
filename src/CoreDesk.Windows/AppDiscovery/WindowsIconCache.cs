using System.Drawing;
using System.Drawing.Imaging;

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
        var cachePath = Path.Combine(_cacheDirectory, $"{SafeFileName(id)}.png");
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
            using var icon = Icon.ExtractAssociatedIcon(source);
            if (icon is null)
            {
                return null;
            }

            using var bitmap = icon.ToBitmap();
            bitmap.Save(cachePath, ImageFormat.Png);
            return cachePath;
        }
        catch
        {
            return null;
        }
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

    private static string SafeFileName(string value)
    {
        return string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
    }
}
