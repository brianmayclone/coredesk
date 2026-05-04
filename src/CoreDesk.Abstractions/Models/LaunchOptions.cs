namespace CoreDesk.Abstractions.Models;

public sealed class LaunchOptions
{
    public bool SafeMode { get; init; }

    public bool ResetConfig { get; init; }

    public bool Diagnostics { get; init; }

    public bool MockHardware { get; init; }

    public bool OverlayMode { get; init; }

    public bool ReplaceExplorerForSession { get; init; }

    public string? LanguageOverride { get; init; }

    public string RunId { get; init; } = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");

    public static LaunchOptions Parse(string? arguments)
    {
        var tokens = SplitArguments(arguments);
        string? language = null;

        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].Equals("--language", StringComparison.OrdinalIgnoreCase) && index + 1 < tokens.Count)
            {
                language = tokens[index + 1];
                index++;
            }
        }

        var safeMode = tokens.Contains("--safe-mode", StringComparer.OrdinalIgnoreCase);
        var overlayMode = tokens.Contains("--overlay-mode", StringComparer.OrdinalIgnoreCase);
        var replaceExplorer = !safeMode && !overlayMode;
        if (tokens.Contains("--replace-explorer-for-session", StringComparer.OrdinalIgnoreCase))
        {
            replaceExplorer = true;
        }

        return new LaunchOptions
        {
            SafeMode = safeMode,
            ResetConfig = tokens.Contains("--reset-config", StringComparer.OrdinalIgnoreCase),
            Diagnostics = tokens.Contains("--diagnostics", StringComparer.OrdinalIgnoreCase),
            MockHardware = tokens.Contains("--mock-hardware", StringComparer.OrdinalIgnoreCase),
            OverlayMode = overlayMode,
            ReplaceExplorerForSession = replaceExplorer,
            LanguageOverride = language
        };
    }

    private static List<string> SplitArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return [];
        }

        return arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
