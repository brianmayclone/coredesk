using CoreDesk.Abstractions.Models;
using CoreDesk.Application.Dock;

namespace CoreDesk.Tests;

public sealed class DockRunningAppMatcherTests
{
    [Fact]
    public void MatchRunningAppIds_PrefersPinnedMatchForDuplicateRunningApp()
    {
        var pinnedExplorer = new AppEntry("pinned-explorer", "File Explorer", AppKind.Win32, ExecutablePath: "explorer.exe");
        var duplicateExplorer = new AppEntry("duplicate-explorer", "Explorer", AppKind.Win32, ExecutablePath: @"C:\Windows\explorer.exe");
        var running = new RunningAppEntry("explorer", @"C:\Windows\explorer.exe", "Downloads");

        var ids = DockRunningAppMatcher.MatchRunningAppIds([running], [duplicateExplorer, pinnedExplorer], [pinnedExplorer]);

        Assert.Equal(["pinned-explorer"], ids);
    }

    [Fact]
    public void IsRunningMatch_MatchesShortcutTargetExecutable()
    {
        var app = new AppEntry("code", "Visual Studio Code", AppKind.Win32, ExecutablePath: @"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe");
        var running = new RunningAppEntry("Code", @"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe", "CoreDesk - Visual Studio Code");

        Assert.True(DockRunningAppMatcher.IsRunningMatch(app, running));
    }
}
