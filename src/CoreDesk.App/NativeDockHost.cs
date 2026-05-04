using System.Diagnostics;

namespace CoreDesk_App;

public sealed class NativeDockHost : IDisposable
{
    private Process? _process;

    public void ShowDock(bool homeMode = false)
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        var startInfo = CreateStartInfo();
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
        }
    }

    public void Dispose() => Close();

    private static ProcessStartInfo? CreateStartInfo()
    {
        var dockPath = FindDockExecutable();
        if (dockPath is not null)
        {
            return new ProcessStartInfo(dockPath)
            {
                Arguments = BuildDockArguments(),
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
            Arguments = $"run --project \"{projectPath}\" -p:Platform=x64 -- {BuildDockArguments()}",
            UseShellExecute = false,
            WorkingDirectory = FindRepositoryRoot() ?? Environment.CurrentDirectory,
            CreateNoWindow = true
        };
    }

    private static string BuildDockArguments()
    {
        var args = new List<string>();
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

        return Directory
            .EnumerateFiles(Path.Combine(root, "src", "CoreDesk.Dock", "bin"), "CoreDesk.Dock.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

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
}
