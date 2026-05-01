using System.Text.Json;
using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Persistence;

public sealed class JsonConfigurationStore : IConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly string _layoutPath;

    public JsonConfigurationStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoreDesk"))
    {
    }

    public JsonConfigurationStore(string rootDirectory)
    {
        Directory.CreateDirectory(rootDirectory);
        _settingsPath = Path.Combine(rootDirectory, "settings.json");
        _layoutPath = Path.Combine(rootDirectory, "layout.json");
    }

    public async Task<CoreDeskSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        return await LoadAsync(_settingsPath, new CoreDeskSettings(), cancellationToken);
    }

    public Task SaveSettingsAsync(CoreDeskSettings settings, CancellationToken cancellationToken = default)
    {
        return SaveAsync(_settingsPath, settings, cancellationToken);
    }

    public async Task<HomeLayout> LoadLayoutAsync(CancellationToken cancellationToken = default)
    {
        return await LoadAsync(_layoutPath, new HomeLayout(), cancellationToken);
    }

    public Task SaveLayoutAsync(HomeLayout layout, CancellationToken cancellationToken = default)
    {
        return SaveAsync(_layoutPath, layout, cancellationToken);
    }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        MoveAside(_settingsPath);
        MoveAside(_layoutPath);
        return Task.CompletedTask;
    }

    public async Task ExportAsync(string targetDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetDirectory);
        await CopyIfExistsAsync(_settingsPath, Path.Combine(targetDirectory, "settings.json"), cancellationToken);
        await CopyIfExistsAsync(_layoutPath, Path.Combine(targetDirectory, "layout.json"), cancellationToken);
    }

    public async Task ImportAsync(string sourceDirectory, CancellationToken cancellationToken = default)
    {
        await CopyIfExistsAsync(Path.Combine(sourceDirectory, "settings.json"), _settingsPath, cancellationToken);
        await CopyIfExistsAsync(Path.Combine(sourceDirectory, "layout.json"), _layoutPath, cancellationToken);
    }

    private static async Task<T> LoadAsync<T>(string path, T fallback, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return fallback;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken) ?? fallback;
        }
        catch (JsonException)
        {
            File.Copy(path, $"{path}.broken-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true);
            return fallback;
        }
    }

    private static async Task SaveAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            File.Copy(path, $"{path}.bak", overwrite: true);
        }

        var tempPath = $"{path}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private static void MoveAside(string path)
    {
        if (File.Exists(path))
        {
            File.Move(path, $"{path}.reset-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true);
        }
    }

    private static async Task CopyIfExistsAsync(string source, string target, CancellationToken cancellationToken)
    {
        if (!File.Exists(source))
        {
            return;
        }

        await using var sourceStream = File.OpenRead(source);
        await using var targetStream = File.Create(target);
        await sourceStream.CopyToAsync(targetStream, cancellationToken);
    }
}
