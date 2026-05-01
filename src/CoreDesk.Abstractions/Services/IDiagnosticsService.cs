namespace CoreDesk.Abstractions.Services;

public interface IDiagnosticsService
{
    string RunId { get; }

    string LogDirectory { get; }

    string ScreenshotDirectory { get; }

    void Info(string message);

    void Error(Exception exception, string message);
}

