using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Testing;

public sealed class MockShellReplacementService : IShellReplacementService
{
    public bool ExplorerShellRunning { get; private set; } = true;

    public bool ConfiguredAsUserShell { get; set; }

    public bool IsSessionReplacementActive { get; private set; }

    public bool ShellReadySignaled { get; private set; }

    public bool IsExplorerShellRunning() => ExplorerShellRunning;

    public bool IsConfiguredAsUserShell(string executablePath) => ConfiguredAsUserShell;

    public void ReplaceExplorerForSession()
    {
        ExplorerShellRunning = false;
        IsSessionReplacementActive = true;
    }

    public void RestoreExplorerForSession()
    {
        ExplorerShellRunning = true;
        IsSessionReplacementActive = false;
    }

    public void SignalShellReady()
    {
        ShellReadySignaled = true;
    }

    public void Dispose()
    {
        RestoreExplorerForSession();
    }
}
