using System.Diagnostics;
using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Windows.AppLaunching;

public sealed class WindowsAppLauncher : IAppLauncher
{
    public Task LaunchAsync(AppEntry app, CancellationToken cancellationToken = default)
    {
        if (app.Id.Equals("desktop", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        if (app.Id.Equals("settings", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true });
            return Task.CompletedTask;
        }

        var launchPath = app.LaunchPath ?? app.ExecutablePath;
        if (string.IsNullOrWhiteSpace(launchPath))
        {
            return Task.CompletedTask;
        }

        Process.Start(new ProcessStartInfo(launchPath)
        {
            Arguments = app.Arguments ?? string.Empty,
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }
}
