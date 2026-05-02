namespace CoreDesk.Abstractions.Models;

public sealed record RunningAppEntry(
    string ProcessName,
    string? ExecutablePath,
    string WindowTitle,
    string? AppUserModelId = null,
    string? PreviewPath = null);
