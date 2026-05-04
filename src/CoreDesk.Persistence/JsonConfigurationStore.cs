using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Persistence;

public sealed class JsonConfigurationStore : IConfigurationStore
{
    private static readonly ConcurrentDictionary<string, StoreState> Stores = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly JsonTypeInfo<CoreDeskSettings> SettingsJsonTypeInfo = CoreDeskJsonSerializerContext.Default.CoreDeskSettings;
    private static readonly JsonTypeInfo<HomeLayout> LayoutJsonTypeInfo = CoreDeskJsonSerializerContext.Default.HomeLayout;

    private readonly StoreState _state;

    public JsonConfigurationStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoreDesk"))
    {
    }

    public JsonConfigurationStore(string rootDirectory)
    {
        Directory.CreateDirectory(rootDirectory);
        var rootKey = Path.GetFullPath(rootDirectory).ToUpperInvariant();
        var lockName = $@"Global\CoreDesk.JsonConfigurationStore.{Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rootKey)))[..16]}";
        _state = Stores.GetOrAdd(rootKey, _ => new StoreState(rootDirectory, lockName));
    }

    public Task<CoreDeskSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        return _state.LoadSettingsAsync(cancellationToken);
    }

    public Task SaveSettingsAsync(CoreDeskSettings settings, CancellationToken cancellationToken = default)
    {
        return _state.SaveSettingsAsync(settings, cancellationToken);
    }

    public Task<HomeLayout> LoadLayoutAsync(CancellationToken cancellationToken = default)
    {
        return _state.LoadLayoutAsync(cancellationToken);
    }

    public Task SaveLayoutAsync(HomeLayout layout, CancellationToken cancellationToken = default)
    {
        return _state.SaveLayoutAsync(layout, cancellationToken);
    }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        return _state.ResetAsync(cancellationToken);
    }

    public Task ExportAsync(string targetDirectory, CancellationToken cancellationToken = default)
    {
        return _state.ExportAsync(targetDirectory, cancellationToken);
    }

    public Task ImportAsync(string sourceDirectory, CancellationToken cancellationToken = default)
    {
        return _state.ImportAsync(sourceDirectory, cancellationToken);
    }

    private sealed class StoreState(string rootDirectory, string lockName)
    {
        private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(25);

        private readonly string _settingsPath = Path.Combine(rootDirectory, "settings.json");
        private readonly string _layoutPath = Path.Combine(rootDirectory, "layout.json");
        private readonly string _lockName = lockName;
        private readonly SemaphoreSlim _cacheGate = new(1, 1);
        private readonly object _writerGate = new();

        private Task _writerTail = Task.CompletedTask;
        private CoreDeskSettings? _settings;
        private HomeLayout? _layout;

        public async Task<CoreDeskSettings> LoadSettingsAsync(CancellationToken cancellationToken)
        {
            await _cacheGate.WaitAsync(cancellationToken);
            try
            {
                _settings ??= await WithDiskLockAsync(
                    () => LoadAsync(_settingsPath, new CoreDeskSettings(), SettingsJsonTypeInfo, cancellationToken),
                    cancellationToken);

                return Clone(_settings, SettingsJsonTypeInfo);
            }
            finally
            {
                _cacheGate.Release();
            }
        }

        public async Task SaveSettingsAsync(CoreDeskSettings settings, CancellationToken cancellationToken)
        {
            var snapshot = Clone(settings, SettingsJsonTypeInfo);
            await _cacheGate.WaitAsync(cancellationToken);
            try
            {
                _settings = snapshot;
            }
            finally
            {
                _cacheGate.Release();
            }

            await EnqueueWriteAsync(_settingsPath, snapshot, SettingsJsonTypeInfo, cancellationToken);
        }

        public async Task<HomeLayout> LoadLayoutAsync(CancellationToken cancellationToken)
        {
            await _cacheGate.WaitAsync(cancellationToken);
            try
            {
                _layout ??= await WithDiskLockAsync(
                    () => LoadAsync(_layoutPath, new HomeLayout(), LayoutJsonTypeInfo, cancellationToken),
                    cancellationToken);

                return Clone(_layout, LayoutJsonTypeInfo);
            }
            finally
            {
                _cacheGate.Release();
            }
        }

        public async Task SaveLayoutAsync(HomeLayout layout, CancellationToken cancellationToken)
        {
            var snapshot = Clone(layout, LayoutJsonTypeInfo);
            await _cacheGate.WaitAsync(cancellationToken);
            try
            {
                _layout = snapshot;
            }
            finally
            {
                _cacheGate.Release();
            }

            await EnqueueWriteAsync(_layoutPath, snapshot, LayoutJsonTypeInfo, cancellationToken);
        }

        public async Task ResetAsync(CancellationToken cancellationToken)
        {
            await _cacheGate.WaitAsync(cancellationToken);
            try
            {
                await EnqueueOperationAsync(
                    () => WithDiskLockAsync(async () =>
                    {
                        await MoveAsideAsync(_settingsPath, cancellationToken);
                        await MoveAsideAsync(_layoutPath, cancellationToken);
                    }, cancellationToken),
                    cancellationToken);

                _settings = new CoreDeskSettings();
                _layout = new HomeLayout();
            }
            finally
            {
                _cacheGate.Release();
            }
        }

        public async Task ExportAsync(string targetDirectory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(targetDirectory);
            await EnqueueOperationAsync(
                () => WithDiskLockAsync(async () =>
                {
                    await CopyIfExistsAsync(_settingsPath, Path.Combine(targetDirectory, "settings.json"), cancellationToken);
                    await CopyIfExistsAsync(_layoutPath, Path.Combine(targetDirectory, "layout.json"), cancellationToken);
                }, cancellationToken),
                cancellationToken);
        }

        public async Task ImportAsync(string sourceDirectory, CancellationToken cancellationToken)
        {
            await _cacheGate.WaitAsync(cancellationToken);
            try
            {
                await EnqueueOperationAsync(
                    () => WithDiskLockAsync(async () =>
                    {
                        await CopyIfExistsAsync(Path.Combine(sourceDirectory, "settings.json"), _settingsPath, cancellationToken);
                        await CopyIfExistsAsync(Path.Combine(sourceDirectory, "layout.json"), _layoutPath, cancellationToken);
                    }, cancellationToken),
                    cancellationToken);

                _settings = null;
                _layout = null;
            }
            finally
            {
                _cacheGate.Release();
            }
        }

        private Task EnqueueWriteAsync<T>(string path, T value, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken)
        {
            return EnqueueOperationAsync(
                () => WithDiskLockAsync(() => SaveAsync(path, value, jsonTypeInfo, cancellationToken), cancellationToken),
                cancellationToken);
        }

        private Task EnqueueOperationAsync(Func<Task> operation, CancellationToken cancellationToken)
        {
            Task queued;
            lock (_writerGate)
            {
                queued = _writerTail.ContinueWith(
                    async previous =>
                    {
                        await ObservePreviousWriteAsync(previous);
                        await operation();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default).Unwrap();
                _writerTail = queued;
            }

            return queued.WaitAsync(cancellationToken);
        }

        private async Task WithDiskLockAsync(Func<Task> operation, CancellationToken cancellationToken)
        {
            await WithDiskLockAsync(async () =>
            {
                await operation();
                return true;
            }, cancellationToken);
        }

        private async Task<T> WithDiskLockAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
        {
            using var semaphore = new Semaphore(1, 1, _lockName);
            var hasCrossProcessLock = false;
            try
            {
                hasCrossProcessLock = await Task.Run(() => semaphore.WaitOne(LockTimeout), cancellationToken);
                if (!hasCrossProcessLock)
                {
                    throw new IOException("Timed out waiting for CoreDesk configuration lock.");
                }

                return await operation();
            }
            finally
            {
                if (hasCrossProcessLock)
                {
                    semaphore.Release();
                }
            }
        }

        private static async Task<T> LoadAsync<T>(string path, T fallback, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken)
        {
            if (!File.Exists(path))
            {
                return fallback;
            }

            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo, cancellationToken) ?? fallback;
            }
            catch (JsonException)
            {
                File.Copy(path, $"{path}.broken-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true);
                return fallback;
            }
        }

        private static async Task SaveAsync<T>(string path, T value, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken)
        {
            if (File.Exists(path))
            {
                File.Copy(path, $"{path}.bak", overwrite: true);
            }

            var tempPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(stream, value, jsonTypeInfo, cancellationToken);
                }

                await ReplaceFileAsync(tempPath, path, cancellationToken);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static Task ReplaceFileAsync(string tempPath, string targetPath, CancellationToken cancellationToken)
        {
            return RetryFileOperationAsync(() =>
            {
                if (File.Exists(targetPath))
                {
                    File.Replace(tempPath, targetPath, $"{targetPath}.replace-bak", ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, targetPath);
                }
            }, cancellationToken);
        }

        private static async Task MoveAsideAsync(string path, CancellationToken cancellationToken)
        {
            if (File.Exists(path))
            {
                await RetryFileOperationAsync(
                    () => File.Move(path, $"{path}.reset-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true),
                    cancellationToken);
            }
        }

        private static async Task CopyIfExistsAsync(string source, string target, CancellationToken cancellationToken)
        {
            if (!File.Exists(source))
            {
                return;
            }

            await using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var targetStream = File.Create(target);
            await sourceStream.CopyToAsync(targetStream, cancellationToken);
        }

        private static async Task RetryFileOperationAsync(Action operation, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    operation();
                    return;
                }
                catch (Exception exception) when (IsTransientFileAccessException(exception) && attempt < 6)
                {
                    var delay = TimeSpan.FromMilliseconds(RetryDelay.TotalMilliseconds * Math.Pow(2, attempt));
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        private static bool IsTransientFileAccessException(Exception exception)
        {
            return exception is IOException or UnauthorizedAccessException;
        }

        private static async Task ObservePreviousWriteAsync(Task previous)
        {
            try
            {
                await previous;
            }
            catch
            {
                // The caller of the failed write observes its own exception; later writes must still run.
            }
        }

        private static T Clone<T>(T value, JsonTypeInfo<T> jsonTypeInfo)
        {
            return JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(value, jsonTypeInfo), jsonTypeInfo)
                ?? throw new InvalidOperationException($"Could not clone {typeof(T).Name}.");
        }
    }
}
