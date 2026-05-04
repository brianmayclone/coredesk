using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CoreDesk_App;

public sealed class NativeDockHost : IDisposable
{
    private Process? _process;
    private bool? _homeMode;

    public void ShowDock(bool homeMode = false)
    {
        if (_process is { HasExited: false })
        {
            if (_homeMode == homeMode)
            {
                return;
            }

            Close();
        }

        _homeMode = homeMode;
        var startInfo = CreateStartInfo(homeMode);
        if (startInfo is null)
        {
            App.Services.Diagnostics.Info("CoreDesk native dock executable was not found; dock will remain hidden.");
            return;
        }

        _process = Process.Start(startInfo);
    }

    public void HideDock()
    {
        Close();
    }

    public void Close()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.CloseMainWindow();
                if (!_process.WaitForExit(1200))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch (Exception exception)
        {
            App.Services.Diagnostics.Error(exception, "Closing native dock failed.");
        }
        finally
        {
            _process.Dispose();
            _process = null;
            _homeMode = null;
        }
    }

    public void Dispose() => Close();

    private static ProcessStartInfo? CreateStartInfo(bool homeMode)
    {
        var dockPath = FindDockExecutable();
        if (dockPath is not null)
        {
            dockPath = CopyDockToRuntimeDirectory(dockPath);
            return new ProcessStartInfo(dockPath)
            {
                Arguments = BuildDockArguments(homeMode),
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(dockPath) ?? Environment.CurrentDirectory
            };
        }

        var projectPath = FindDockProject();
        if (projectPath is null)
        {
            return null;
        }

        return new ProcessStartInfo("dotnet")
        {
            Arguments = $"run --project \"{projectPath}\" -p:Platform={GetPlatformName()} -- {BuildDockArguments(homeMode)}",
            UseShellExecute = false,
            WorkingDirectory = FindRepositoryRoot() ?? Environment.CurrentDirectory,
            CreateNoWindow = true
        };
    }

    private static string BuildDockArguments(bool homeMode)
    {
        var args = new List<string>
        {
            "--parent-pid",
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        if (homeMode)
        {
            args.Add("--home-mode");
        }

        if (App.Services.Options.Diagnostics)
        {
            args.Add("--diagnostics");
        }

        if (App.Services.Options.MockHardware)
        {
            args.Add("--mock-hardware");
        }

        if (!string.IsNullOrWhiteSpace(App.Services.Options.LanguageOverride))
        {
            args.Add("--language");
            args.Add(App.Services.Options.LanguageOverride);
        }

        return string.Join(' ', args.Select(QuoteIfNeeded));
    }

    private static string? FindDockExecutable()
    {
        var appDirectory = AppContext.BaseDirectory;
        var adjacent = Path.Combine(appDirectory, "CoreDesk.Dock.exe");
        if (File.Exists(adjacent))
        {
            return adjacent;
        }

        var root = FindRepositoryRoot();
        if (root is null)
        {
            return null;
        }

        var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var preferredRuntime = architecture switch
        {
            "x64" => "win-x64",
            "x86" => "win-x86",
            "arm64" => "win-arm64",
            _ => "win-x64"
        };

        var candidates = Directory
            .EnumerateFiles(Path.Combine(root, "src", "CoreDesk.Dock", "bin"), "CoreDesk.Dock.exe", SearchOption.AllDirectories)
            .ToList();

        return candidates
            .Where(path => path.Contains(preferredRuntime, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Concat(candidates.OrderByDescending(File.GetLastWriteTimeUtc))
            .FirstOrDefault();
    }

    private static string GetPlatformName() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.Arm64 => "ARM64",
            _ => "x64"
        };

    private static string? FindDockProject()
    {
        var root = FindRepositoryRoot();
        if (root is null)
        {
            return null;
        }

        var projectPath = Path.Combine(root, "src", "CoreDesk.Dock", "CoreDesk.Dock.csproj");
        return File.Exists(projectPath) ? projectPath : null;
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CoreDesk.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string QuoteIfNeeded(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }

    private static string CopyDockToRuntimeDirectory(string dockExecutablePath)
    {
        try
        {
            var sourceDirectory = Path.GetDirectoryName(dockExecutablePath);
            var root = FindRepositoryRoot();
            if (sourceDirectory is null || root is null)
            {
                return dockExecutablePath;
            }

            var runtimeDirectory = Path.Combine(root, "artifacts", "runtime", "CoreDesk.Dock", $"{Environment.ProcessId}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(runtimeDirectory);

            foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
                var destinationPath = Path.Combine(runtimeDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath, overwrite: true);
            }

            var runtimeExecutable = Path.Combine(runtimeDirectory, Path.GetFileName(dockExecutablePath));
            return File.Exists(runtimeExecutable) ? runtimeExecutable : dockExecutablePath;
        }
        catch (Exception exception)
        {
            App.Services.Diagnostics.Error(exception, "Copying native dock to runtime directory failed.");
            return dockExecutablePath;
        }
    }
}
