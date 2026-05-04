using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;
using System.Diagnostics;

namespace CoreDesk.Windows.AppDiscovery;

public sealed class StartMenuAppDiscoveryService : IAppDiscoveryService
{
    private readonly WindowsIconCache _iconCache = new();

    public async Task<IReadOnlyList<AppEntry>> DiscoverAppsAsync(CancellationToken cancellationToken = default)
    {
        var entries = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);

        AddSystemAction(entries, "desktop", "Desktop");
        AddSystemAction(entries, "settings", "Settings");
        AddSystemAction(entries, "explorer", "File Explorer", "explorer.exe");
        AddKnownExecutable(entries, "notepad", "Notepad", "notepad.exe");
        AddKnownExecutable(entries, "calculator", "Calculator", "calc.exe");
        AddKnownExecutable(entries, "paint", "Paint", "mspaint.exe");
        AddKnownExecutable(entries, "terminal", "Terminal", "wt.exe");
        AddKnownExecutable(entries, "powershell", "PowerShell", "powershell.exe");

        foreach (var folder in GetStartMenuFolders())
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var shortcut in EnumerateShortcuts(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileNameWithoutExtension(shortcut);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var id = StableId(shortcut);
                var resolved = WindowsShortcutResolver.TryResolve(shortcut);
                var iconPath = _iconCache.GetOrCreateIconPath(id, resolved.TargetPath, shortcut);
                entries.TryAdd(id, new AppEntry(
                    id,
                    CleanDisplayName(name),
                    AppKind.Win32,
                    ExecutablePath: resolved.TargetPath ?? shortcut,
                    IconPath: iconPath,
                    LaunchPath: shortcut));
            }
        }

        foreach (var storeApp in await StoreAppDiscoveryService.DiscoverAppsAsync(_iconCache, cancellationToken))
        {
            entries.TryAdd(storeApp.AppUserModelId ?? storeApp.Id, storeApp);
        }

        return [.. entries.Values.OrderBy(entry => entry.DisplayName)];
    }

    private static IEnumerable<string> GetStartMenuFolders()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
    }

    private void AddSystemAction(Dictionary<string, AppEntry> entries, string id, string name, string? executable = null)
    {
        var iconPath = _iconCache.GetOrCreateIconPath(id, executable, null);
        entries[id] = new AppEntry(id, name, executable is null ? AppKind.SystemAction : AppKind.Win32, ExecutablePath: executable, IconPath: iconPath);
    }

    private static IEnumerable<string> EnumerateShortcuts(string folder)
    {
        var pending = new Stack<string>();
        pending.Push(folder);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> files;
            IEnumerable<string> directories;

            try
            {
                files = Directory.EnumerateFiles(current, "*.lnk");
                directories = Directory.EnumerateDirectories(current);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var directory in directories)
            {
                pending.Push(directory);
            }
        }
    }

    private void AddKnownExecutable(Dictionary<string, AppEntry> entries, string id, string name, string executable)
    {
        if (CanResolveExecutable(executable))
        {
            var iconPath = _iconCache.GetOrCreateIconPath(id, executable, null);
            entries.TryAdd(id, new AppEntry(id, name, AppKind.Win32, ExecutablePath: executable, IconPath: iconPath));
        }
    }

    private static bool CanResolveExecutable(string executable)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo("where.exe", executable)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            process.Start();
            process.WaitForExit(1500);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string StableId(string value)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    }

    private static string CleanDisplayName(string value)
    {
        return value.Replace(".VisualElementsManifest", "", StringComparison.OrdinalIgnoreCase).Trim();
    }
}
