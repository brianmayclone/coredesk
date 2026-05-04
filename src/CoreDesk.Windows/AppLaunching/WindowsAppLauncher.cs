using System.Diagnostics;
using System.Runtime.InteropServices;
using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Windows.AppLaunching;

public sealed class WindowsAppLauncher(IDiagnosticsService diagnostics) : IAppLauncher
{
    public Task LaunchAsync(AppEntry app, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (app.Id.Equals("desktop", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        if (app.Id.Equals("settings", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true });
            return Task.CompletedTask;
        }

        if (!string.IsNullOrWhiteSpace(app.AppUserModelId))
        {
            LaunchPackagedApp(app);
            return Task.CompletedTask;
        }

        var launchPath = app.LaunchPath ?? app.ExecutablePath;
        if (string.IsNullOrWhiteSpace(launchPath))
        {
            throw new InvalidOperationException($"App '{app.DisplayName}' has no launch path.");
        }

        var process = Process.Start(new ProcessStartInfo(launchPath)
        {
            Arguments = app.Arguments ?? string.Empty,
            UseShellExecute = true
        });
        diagnostics.Info($"Started Win32 app '{app.DisplayName}' from '{launchPath}'. ProcessId={process?.Id.ToString() ?? "<shell>"}.");

        return Task.CompletedTask;
    }

    private void LaunchPackagedApp(AppEntry app)
    {
        try
        {
            var activationManagerType = Type.GetTypeFromCLSID(ApplicationActivationManagerClsid, throwOnError: true)!;
            var activator = (IApplicationActivationManager)Activator.CreateInstance(activationManagerType)!;
            var result = activator.ActivateApplication(app.AppUserModelId!, app.Arguments ?? string.Empty, ActivateOptions.None, out var processId);
            if (result < 0)
            {
                Marshal.ThrowExceptionForHR(result);
            }

            diagnostics.Info($"Activated packaged app '{app.DisplayName}' via IApplicationActivationManager. ProcessId={processId}.");
            return;
        }
        catch (Exception exception)
        {
            diagnostics.Error(exception, $"Packaged activation failed for '{app.DisplayName}', falling back to shell namespace.");
        }

        var shellPath = $@"shell:AppsFolder\{app.AppUserModelId}";
        var process = Process.Start(new ProcessStartInfo(shellPath) { UseShellExecute = true });
        diagnostics.Info($"Activated packaged app '{app.DisplayName}' via shell namespace fallback. ProcessId={process?.Id.ToString() ?? "<shell>"}.");
    }

    private static readonly Guid ApplicationActivationManagerClsid = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            ActivateOptions options,
            out uint processId);

        int ActivateForFile();

        int ActivateForProtocol();
    }

    [Flags]
    private enum ActivateOptions
    {
        None = 0x00000000
    }
}
