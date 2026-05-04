using System.Text.Json;
using System.Security.Cryptography;
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
    private readonly string _mutexName;

    public JsonConfigurationStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoreDesk"))
    {
    }

    public JsonConfigurationStore(string rootDirectory)
    {
        Directory.CreateDirectory(rootDirectory);
        _settingsPath = Path.Combine(rootDirectory, "settings.json");
        _layoutPath = Path.Combine(rootDirectory, "layout.json");
        _mutexName = $@"Global\CoreDesk.JsonConfigurationStore.{Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(rootDirectory).ToUpperInvariant())))[..16]}";
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

    private async Task SaveAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        using var semaphore = new Semaphore(1, 1, _mutexName);
        var hasLock = false;
        try
        {
            hasLock = semaphore.WaitOne(TimeSpan.FromSeconds(8));
            if (!hasLock)
            {
                throw new IOException($"Timed out waiting for CoreDesk configuration lock: {path}");
            }

            if (File.Exists(path))
            {
                File.Copy(path, $"{path}.bak", overwrite: true);
            }

            var tempPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken);
            }

            ReplaceFile(tempPath, path);
        }
        finally
        {
            if (hasLock)
            {
                semaphore.Release();
            }
        }
    }

    private static void ReplaceFile(string tempPath, string targetPath)
    {
        try
        {
            if (File.Exists(targetPath))
            {
                File.Replace(tempPath, targetPath, $"{targetPath}.replace-bak", ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }
        }
        catch
        {
            File.Move(tempPath, targetPath, overwrite: true);
        }
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
