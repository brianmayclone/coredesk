using CoreDesk.Abstractions.Models;

namespace CoreDesk.Tests;

public sealed class LaunchOptionsTests
{
    [Fact]
    public void Parse_ReadsDiagnosticSafeModeAndLanguageFlags()
    {
        var options = LaunchOptions.Parse("--diagnostics --mock-hardware --safe-mode --language de");

        Assert.True(options.Diagnostics);
        Assert.True(options.MockHardware);
        Assert.True(options.SafeMode);
        Assert.Equal("de", options.LanguageOverride);
        Assert.False(string.IsNullOrWhiteSpace(options.RunId));
    }
}

