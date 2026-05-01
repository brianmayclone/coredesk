namespace CoreDesk.Abstractions.Services;

public interface IAutostartService
{
    bool IsEnabled();

    void SetEnabled(bool enabled, string executablePath, string arguments = "");
}

