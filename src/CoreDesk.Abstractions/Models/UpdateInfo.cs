namespace CoreDesk.Abstractions.Models;

public sealed record UpdateInfo(
    Version CurrentVersion,
    Version LatestVersion,
    bool IsUpdateAvailable,
    string? ReleaseNotes,
    Uri? InstallerUri,
    string? InstallerSha256,
    long? InstallerSizeBytes);
