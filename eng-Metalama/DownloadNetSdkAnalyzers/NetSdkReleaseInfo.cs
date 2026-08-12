using System.Net.Http.Json;
using System.Text.Json;
using NuGet.Versioning;

namespace DownloadNetSdkAnalyzers;

static class NetSdkReleaseInfo
{
    private const string ReleasesIndexUrl = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json";

    static readonly HttpClient s_httpClient = new();

    static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
        Converters = { new SemanticVersionConverter(), new SemanticVersionDictionaryKeyConverterFactory() },
        WriteIndented = true,
    };

    public static async Task<NetSdkDownloader> GetLatestSdkDownloaderForRoslynVersionAsync(SemanticVersion requestedRoslynVersion)
    {
        var (foundReleaseVersion, foundRelease) = await GetLatestSdkForRoslynVersionAsync(requestedRoslynVersion);

        return await NetSdkDownloader.CreateAsync(foundRelease.SdkZipUrl, foundReleaseVersion);
    }

    public static async Task<SemanticVersion> GetLatestSdkVersionForRoslynVersionAsync(SemanticVersion requestedRoslynVersion)
    {
        var (foundReleaseVersion, _) = await GetLatestSdkForRoslynVersionAsync(requestedRoslynVersion);

        return foundReleaseVersion;
    }

    private static async Task<KeyValuePair<SemanticVersion, NetSdkRelease>> GetLatestSdkForRoslynVersionAsync(SemanticVersion requestedRoslynVersion)
    {
        // Microsoft.CodeAnalysis.csproj multi-targets, and its AddDotnetSdkAssemblyAttribute target runs once per
        // TargetFramework, so several instances of this tool run at the same time against the same directory. They
        // all read, and possibly rewrite, net-sdk-releases.json below. Hold an exclusive lock for the whole
        // operation: the first process refreshes the cache and the others then find it complete, which also stops
        // them from downloading the same SDK archives over and over.
        using var cacheLock = await AcquireCacheLockAsync();

        return await GetLatestSdkForRoslynVersionCoreAsync(requestedRoslynVersion);
    }

    /// <summary>
    /// Takes an exclusive, cross-process lock on the cache. A lock file is used rather than a <see cref="Mutex"/>
    /// because a mutex is owned by the thread that took it, and the awaits below can resume on another thread.
    /// </summary>
    private static async Task<FileStream> AcquireCacheLockAsync()
    {
        const string lockPath = "net-sdk-releases.lock";
        var timeout = TimeSpan.FromMinutes(10);
        var started = DateTime.UtcNow;

        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow - started < timeout)
            {
                // Another instance holds the lock. It may be downloading SDK archives, which takes a while.
                await Task.Delay(500);
            }
        }
    }

    private static async Task<KeyValuePair<SemanticVersion, NetSdkRelease>> GetLatestSdkForRoslynVersionCoreAsync(SemanticVersion requestedRoslynVersion)
    {
        // TODO: make this more efficient by not downloading releases-index.json and all the releases.json every time?

        var netSdkReleasesPath = "net-sdk-releases.json";

        NetSdkReleasesDocument? netSdkReleases = null;

        if (File.Exists(netSdkReleasesPath))
        {
            using var stream = File.OpenRead(netSdkReleasesPath);

            try
            {
                netSdkReleases = JsonSerializer.Deserialize<NetSdkReleasesDocument>(stream, s_jsonOptions)!;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
        }

        if (netSdkReleases == null)
        {
            netSdkReleases = new(new());
        }

        var canAnswerFromCache = netSdkReleases.Releases.Any(
            kvp => (!kvp.Key.IsPrerelease || requestedRoslynVersion.IsPrerelease) && kvp.Value.RoslynVersion <= requestedRoslynVersion);

        try
        {
            await RefreshAsync(netSdkReleases, netSdkReleasesPath);
        }
        catch (Exception ex) when (canAnswerFromCache)
        {
            // Every build contacts dotnetcli.blob.core.windows.net to look for SDK releases newer than the cache.
            // A hiccup there used to abort the build with an unhandled exception and no message, because the
            // MSBuild target that runs this tool discards standard error. The cached list already answers the
            // query, so report the problem and carry on rather than failing the build over a transient error.
            Console.Error.WriteLine(
                $"warning: could not refresh the .NET SDK release list ({ex.GetType().Name}: {ex.Message}). "
                + $"Using the cached list in '{Path.GetFullPath(netSdkReleasesPath)}', which may omit recent SDKs.");
        }

        // Only consider preview SDKs if the requested version is also preview.
        return netSdkReleases.Releases
            .Last(kvp => (!kvp.Key.IsPrerelease || requestedRoslynVersion.IsPrerelease) && kvp.Value.RoslynVersion <= requestedRoslynVersion);
    }

    private static async Task RefreshAsync(NetSdkReleasesDocument netSdkReleases, string netSdkReleasesPath)
    {
        var releasesIndex = await GetFromJsonWithRetryAsync<ReleasesIndexDocument>(ReleasesIndexUrl);

        // The four most recent .Net releases should be sufficient: preview (e.g. 9.0), current LTS (8.0), out-of support (7.0) and the previous LTS (6.0).
        var channels = releasesIndex!.Channels.Take(4);

        var change = false;

        foreach (var channel in channels)
        {
            var releases = await GetFromJsonWithRetryAsync<ReleasesDocument>(channel.ReleasesJsonUrl);

            foreach (var release in releases!.Releases)
            {
                var sdkVersion = release.Sdk.VersionDisplay;

                // Only consider pre-release versions if the .Net version is still in preview.
                if (sdkVersion.IsPrerelease && channel.SupportPhase is not ("preview" or "go-live"))
                {
                    continue;
                }

                if (!netSdkReleases.Releases.ContainsKey(sdkVersion))
                {
                    var sdkZip = release.Sdk.Files.Single(file => file.Name == "dotnet-sdk-win-x64.zip");

                    using var downloader = await NetSdkDownloader.CreateAsync(sdkZip.Url, sdkVersion);

                    netSdkReleases.Releases.Add(
                        sdkVersion, new(sdkZip.Url, downloader.GetCodeAnalysisVersion()));

                    change = true;
                }
            }
        }

        if (change)
        {
            // Write to a temporary file and move it into place, so a reader never observes a half-written file
            // and an interrupted run cannot leave the cache truncated.
            var temporaryPath = netSdkReleasesPath + ".tmp";

            using (var stream = File.Create(temporaryPath))
            {
                JsonSerializer.Serialize(stream, netSdkReleases, s_jsonOptions);
            }

            File.Move(temporaryPath, netSdkReleasesPath, overwrite: true);
        }
    }

    private static async Task<T?> GetFromJsonWithRetryAsync<T>(string url)
    {
        const int attempts = 4;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await s_httpClient.GetFromJsonAsync<T>(url, s_jsonOptions);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && attempt < attempts)
            {
                Console.Error.WriteLine($"warning: '{url}' failed ({ex.Message}), retrying {attempt}/{attempts - 1}.");

                await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
        }
    }
}
