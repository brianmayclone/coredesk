namespace CoreDesk.Abstractions.Models;

public sealed record AppEntry(
    string Id,
    string DisplayName,
    AppKind Kind,
    string? ExecutablePath = null,
    string? Arguments = null,
    string? AppUserModelId = null,
    string? IconPath = null,
    string? LaunchPath = null,
    bool IsHidden = false);
