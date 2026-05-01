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

        if (string.IsNullOrWhiteSpace(app.ExecutablePath))
        {
            return Task.CompletedTask;
        }

        Process.Start(new ProcessStartInfo(app.ExecutablePath)
        {
            Arguments = app.Arguments ?? string.Empty,
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }
}

