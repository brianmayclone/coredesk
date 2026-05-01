using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

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
            var resolved = TryResolveShortcut(shortcutPath);
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

    private static (string? TargetPath, string? IconPath) TryResolveShortcut(string shortcutPath)
    {
        try
        {
            var shellLinkType = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"));
            if (shellLinkType is null)
            {
                return (null, null);
            }

            var shellLink = (IShellLinkW)Activator.CreateInstance(shellLinkType)!;
            ((IPersistFile)shellLink).Load(shortcutPath, 0);

            var targetBuilder = new StringBuilder(1024);
            shellLink.GetPath(targetBuilder, targetBuilder.Capacity, IntPtr.Zero, 0);
            var target = targetBuilder.ToString();

            var iconBuilder = new StringBuilder(1024);
            shellLink.GetIconLocation(iconBuilder, iconBuilder.Capacity, out _);
            var icon = iconBuilder.ToString();

            return (Expand(icon.Length == 0 ? target : target), Expand(icon));
        }
        catch
        {
            return (null, null);
        }
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

    private static string? Expand(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : Environment.ExpandEnvironmentVariables(value);
    }

    private static string SafeFileName(string value)
    {
        return string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
