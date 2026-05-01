namespace CoreDesk.Abstractions.Models;

public sealed class LaunchOptions
{
    public bool SafeMode { get; init; }

    public bool ResetConfig { get; init; }

    public bool Diagnostics { get; init; }

    public bool MockHardware { get; init; }

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

        return new LaunchOptions
        {
            SafeMode = tokens.Contains("--safe-mode", StringComparer.OrdinalIgnoreCase),
            ResetConfig = tokens.Contains("--reset-config", StringComparer.OrdinalIgnoreCase),
            Diagnostics = tokens.Contains("--diagnostics", StringComparer.OrdinalIgnoreCase),
            MockHardware = tokens.Contains("--mock-hardware", StringComparer.OrdinalIgnoreCase),
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

