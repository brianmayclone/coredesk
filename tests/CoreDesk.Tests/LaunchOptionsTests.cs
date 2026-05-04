using CoreDesk.Abstractions.Models;

namespace CoreDesk.Tests;

public sealed class LaunchOptionsTests
{
    [Fact]
    public void Parse_ReadsDiagnosticSafeModeAndLanguageFlags()
    {
        var options = LaunchOptions.Parse("--diagnostics --mock-hardware --safe-mode --replace-explorer-for-session --language de");

        Assert.True(options.Diagnostics);
        Assert.True(options.MockHardware);
        Assert.True(options.SafeMode);
        Assert.True(options.ReplaceExplorerForSession);
        Assert.Equal("de", options.LanguageOverride);
        Assert.False(string.IsNullOrWhiteSpace(options.RunId));
    }

    [Fact]
    public void Parse_DefaultsToSessionShellReplacement()
    {
        var options = LaunchOptions.Parse("");

        Assert.True(options.ReplaceExplorerForSession);
        Assert.False(options.OverlayMode);
    }

    [Fact]
    public void Parse_OverlayModeDisablesSessionShellReplacement()
    {
        var options = LaunchOptions.Parse("--overlay-mode");

        Assert.False(options.ReplaceExplorerForSession);
        Assert.True(options.OverlayMode);
    }
}
