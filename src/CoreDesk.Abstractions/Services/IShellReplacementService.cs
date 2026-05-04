namespace CoreDesk.Abstractions.Services;

public interface IShellReplacementService : IDisposable
{
    bool IsExplorerShellRunning();

    bool IsConfiguredAsUserShell(string executablePath);

    bool IsSessionReplacementActive { get; }

    void ReplaceExplorerForSession();

    void RestoreExplorerForSession();

    void SignalShellReady();
}
