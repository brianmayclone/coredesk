using CoreDesk.Abstractions.Models;

namespace CoreDesk.Windows.AppDiscovery;

internal static class StoreAppDiscoveryService
{
    public static async Task<IReadOnlyList<AppEntry>> DiscoverAppsAsync(CancellationToken cancellationToken)
    {
        var apps = new List<AppEntry>();

        try
        {
            foreach (var package in new global::Windows.Management.Deployment.PackageManager().FindPackagesForUser(string.Empty))
            {
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyList<global::Windows.ApplicationModel.Core.AppListEntry> entries;
                try
                {
                    entries = await package.GetAppListEntriesAsync();
                }
                catch
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var appUserModelId = entry.AppUserModelId;
                    if (string.IsNullOrWhiteSpace(appUserModelId))
                    {
                        continue;
                    }

                    var displayName = entry.DisplayInfo.DisplayName;
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = package.DisplayName;
                    }

                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = package.Id.Name;
                    }

                    apps.Add(new AppEntry(
                        $"store-{StableId(appUserModelId)}",
                        displayName.Trim(),
                        AppKind.Store,
                        AppUserModelId: appUserModelId,
                        LaunchPath: $@"shell:AppsFolder\{appUserModelId}"));
                }
            }
        }
        catch
        {
            return [];
        }

        return apps;
    }

    private static string StableId(string value)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    }
}
