using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Diagnostics;

public sealed class FileDiagnosticsService : IDiagnosticsService
{
    private readonly object _sync = new();
    private readonly string _logPath;

    public FileDiagnosticsService(LaunchOptions options, string? repositoryRoot = null)
    {
        RunId = options.RunId;
        var root = repositoryRoot ?? AppContext.BaseDirectory;
        LogDirectory = Path.Combine(root, "artifacts", "logs", RunId);
        ScreenshotDirectory = Path.Combine(root, "artifacts", "screenshots", RunId);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(ScreenshotDirectory);
        _logPath = Path.Combine(LogDirectory, "coredesk.log");
    }

    public string RunId { get; }

    public string LogDirectory { get; }

    public string ScreenshotDirectory { get; }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Error(Exception exception, string message)
    {
        Write("ERROR", $"{message}: {exception}");
    }

    private void Write(string level, string message)
    {
        lock (_sync)
        {
            File.AppendAllText(_logPath, $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}");
        }
    }
}

