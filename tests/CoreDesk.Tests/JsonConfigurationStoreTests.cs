using CoreDesk.Abstractions.Models;
using CoreDesk.Persistence;

namespace CoreDesk.Tests;

public sealed class JsonConfigurationStoreTests
{
    [Fact]
    public async Task LoadSettingsAsync_ReturnsDefaults_WhenFileDoesNotExist()
    {
        var root = CreateTempRoot();
        var store = new JsonConfigurationStore(root);

        var settings = await store.LoadSettingsAsync();

        Assert.Equal("en", settings.Language);
        Assert.True(settings.HideTaskbarInTouchMode);
    }

    [Fact]
    public async Task SaveAndLoadSettingsAsync_RoundTripsSettings()
    {
        var root = CreateTempRoot();
        var store = new JsonConfigurationStore(root);

        await store.SaveSettingsAsync(new CoreDeskSettings
        {
            Language = "de",
            DockPosition = DockPosition.Left,
            AutoStartWithWindows = true
        });

        var settings = await store.LoadSettingsAsync();

        Assert.Equal("de", settings.Language);
        Assert.Equal(DockPosition.Left, settings.DockPosition);
        Assert.True(settings.AutoStartWithWindows);
    }

    [Fact]
    public async Task LoadLayoutAsync_BacksUpBrokenJson_AndReturnsDefault()
    {
        var root = CreateTempRoot();
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "layout.json"), "{ broken");
        var store = new JsonConfigurationStore(root);

        var layout = await store.LoadLayoutAsync();

        Assert.Single(layout.Pages);
        Assert.NotEmpty(Directory.EnumerateFiles(root, "layout.json.broken-*"));
    }

    [Fact]
    public async Task ResetAsync_MovesExistingConfigurationAside()
    {
        var root = CreateTempRoot();
        var store = new JsonConfigurationStore(root);
        await store.SaveSettingsAsync(new CoreDeskSettings { Language = "de" });

        await store.ResetAsync();

        Assert.Equal("en", (await store.LoadSettingsAsync()).Language);
        Assert.NotEmpty(Directory.EnumerateFiles(root, "settings.json.reset-*"));
    }

    [Fact]
    public async Task ExportAndImportAsync_RoundTripsConfigurationFiles()
    {
        var sourceRoot = CreateTempRoot();
        var exportRoot = CreateTempRoot();
        var targetRoot = CreateTempRoot();
        var source = new JsonConfigurationStore(sourceRoot);
        var target = new JsonConfigurationStore(targetRoot);

        await source.SaveSettingsAsync(new CoreDeskSettings { Language = "de" });
        await source.ExportAsync(exportRoot);
        await target.ImportAsync(exportRoot);

        Assert.Equal("de", (await target.LoadSettingsAsync()).Language);
    }

    [Fact]
    public async Task ConcurrentConsumers_CanReadAndWriteSharedConfiguration()
    {
        var root = CreateTempRoot();
        var stores = Enumerable.Range(0, 8)
            .Select(_ => new JsonConfigurationStore(root))
            .ToArray();

        var tasks = Enumerable.Range(0, 80)
            .Select(async index =>
            {
                var store = stores[index % stores.Length];
                if (index % 3 == 0)
                {
                    await store.SaveSettingsAsync(new CoreDeskSettings
                    {
                        Language = index % 2 == 0 ? "de" : "en",
                        AutoStartWithWindows = index % 5 == 0
                    });
                    return;
                }

                _ = await store.LoadSettingsAsync();
            });

        await Task.WhenAll(tasks);

        var settings = await stores[0].LoadSettingsAsync();
        Assert.Contains(settings.Language, new[] { "en", "de" });
    }

    [Fact]
    public async Task LoadSettingsAsync_ReturnsCopyOfCachedSettings()
    {
        var root = CreateTempRoot();
        var store = new JsonConfigurationStore(root);
        await store.SaveSettingsAsync(new CoreDeskSettings { Language = "de" });

        var loaded = await store.LoadSettingsAsync();
        loaded.Language = "en";

        Assert.Equal("de", (await store.LoadSettingsAsync()).Language);
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "CoreDesk.Tests", Guid.NewGuid().ToString("N"));
    }
}
