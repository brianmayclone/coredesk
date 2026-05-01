using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Testing;

public sealed class MockAutostartService : IAutostartService
{
    public bool Enabled { get; private set; }

    public string? ExecutablePath { get; private set; }

    public string Arguments { get; private set; } = string.Empty;

    public bool IsEnabled() => Enabled;

    public void SetEnabled(bool enabled, string executablePath, string arguments = "")
    {
        Enabled = enabled;
        ExecutablePath = executablePath;
        Arguments = arguments;
    }
}
