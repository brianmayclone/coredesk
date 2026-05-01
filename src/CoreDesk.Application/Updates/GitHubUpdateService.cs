using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace CoreDesk.Application.Updates;

public sealed class GitHubUpdateService(
    string repository,
    IDiagnosticsService diagnostics,
    HttpClient? httpClient = null) : IUpdateService
{
    private readonly HttpClient _http = httpClient ?? CreateHttpClient();

    public Version CurrentVersion { get; } = GetCurrentVersion();

    public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var releaseUri = new Uri($"https://api.github.com/repos/{repository}/releases/latest");
        using var response = await _http.GetAsync(releaseUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tagName = root.GetProperty("tag_name").GetString() ?? "0.0.0";
        var latestVersion = ParseVersion(tagName);
        var releaseNotes = root.TryGetProperty("body", out var body) ? body.GetString() : null;

        var manifest = await TryLoadManifestAsync(root, cancellationToken);
        var installer = manifest?.FindInstallerForCurrentArchitecture();
        var installerUri = installer?.InstallerUri ?? manifest?.InstallerUri ?? FindAsset(root, GetInstallerAssetSuffix()) ?? FindAsset(root, ".exe");
        var installerSha256 = installer?.InstallerSha256 ?? manifest?.InstallerSha256;
        var installerSize = installer?.InstallerSizeBytes ?? manifest?.InstallerSizeBytes;

        return new UpdateInfo(
            CurrentVersion,
            latestVersion,
            latestVersion > CurrentVersion,
            manifest?.ReleaseNotes ?? releaseNotes,
            installerUri,
            installerSha256,
            installerSize);
    }

    public async Task StartUpdateAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!update.IsUpdateAvailable || update.InstallerUri is null)
        {
            return;
        }

        var installerPath = Path.Combine(Path.GetTempPath(), $"CoreDesk-Setup-{update.LatestVersion}.exe");
        await DownloadAsync(update.InstallerUri, installerPath, progress, cancellationToken);
        if (!string.IsNullOrWhiteSpace(update.InstallerSha256))
        {
            await VerifySha256Async(installerPath, update.InstallerSha256, cancellationToken);
        }

        diagnostics.Info($"Starting CoreDesk update installer: {installerPath}");
        Process.Start(new ProcessStartInfo(installerPath)
        {
            Arguments = "/VERYSILENT /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
            UseShellExecute = true
        });
    }

    private async Task<UpdateManifest?> TryLoadManifestAsync(JsonElement releaseRoot, CancellationToken cancellationToken)
    {
        var manifestUri = FindAsset(releaseRoot, "coredesk-update.json");
        if (manifestUri is null)
        {
            return null;
        }

        try
        {
            await using var stream = await _http.GetStreamAsync(manifestUri, cancellationToken);
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, JsonOptions, cancellationToken);
            return manifest;
        }
        catch (Exception exception)
        {
            diagnostics.Error(exception, "Failed to load update manifest.");
            return null;
        }
    }

    private static Uri? FindAsset(JsonElement releaseRoot, string suffix)
    {
        if (!releaseRoot.TryGetProperty("assets", out var assets))
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var url = asset.GetProperty("browser_download_url").GetString();
            return string.IsNullOrWhiteSpace(url) ? null : new Uri(url);
        }

        return null;
    }

    private async Task DownloadAsync(Uri uri, string path, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(path);
        var buffer = new byte[1024 * 128];
        var readTotal = 0L;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            readTotal += read;
            if (total is > 0)
            {
                progress?.Report(Math.Clamp(readTotal / (double)total.Value, 0, 1));
            }
        }

        progress?.Report(1);
    }

    private static async Task VerifySha256Async(string path, string expectedSha256, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Downloaded installer hash does not match the release manifest.");
        }
    }

    private static Version ParseVersion(string value)
    {
        var clean = value.Trim().TrimStart('v', 'V');
        return Version.TryParse(clean, out var version) ? version : new Version(0, 0, 0);
    }

    private static Version GetCurrentVersion()
    {
        var informational = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0";
        return ParseVersion(informational.Split('+')[0]);
    }

    private static string GetInstallerAssetSuffix()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "-arm64.exe",
            Architecture.X64 => "-x64.exe",
            Architecture.X86 => "-x86.exe",
            _ => ".exe"
        };
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CoreDesk-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record UpdateManifest(
        string Version,
        string? ReleaseNotes,
        Uri? InstallerUri,
        string? InstallerSha256,
        long? InstallerSizeBytes,
        IReadOnlyList<UpdateManifestInstaller>? Installers)
    {
        public UpdateManifestInstaller? FindInstallerForCurrentArchitecture()
        {
            var architecture = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                _ => null
            };

            return string.IsNullOrWhiteSpace(architecture)
                ? null
                : Installers?.FirstOrDefault(installer =>
                    installer.Architecture.Equals(architecture, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed record UpdateManifestInstaller(
        string Architecture,
        string Runtime,
        Uri? InstallerUri,
        string? InstallerSha256,
        long? InstallerSizeBytes);
}
