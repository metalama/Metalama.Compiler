using DownloadNetSdkAnalyzers;
using NuGet.Versioning;

var requestedRoslynVersion = SemanticVersion.Parse(args[0]);

if (args is [_, "-sdk-version", ..])
{
    Console.WriteLine(await NetSdkReleaseInfo.GetLatestSdkVersionForRoslynVersionAsync(requestedRoslynVersion));
    return;
}

using var sdkDownloader = await NetSdkReleaseInfo.GetLatestSdkDownloaderForRoslynVersionAsync(requestedRoslynVersion);

var directory = Path.Combine(Path.GetTempPath(), "Metalama", "SdkAnalyzers", sdkDownloader.SdkVersion.ToString());

var completedFilePath = Path.Combine(directory, ".completed");

// Metalama.Compiler.Package.csproj invokes this tool from a target that runs once per TargetFramework, so up to
// four instances reach this point together. Without a lock they each find the directory missing or incomplete,
// delete what a sibling is in the middle of writing, and open the same analyzer files for writing. On a machine
// where the directory is already complete none of that happens, which is why it only bites fresh build agents.
using var downloadLock = await CrossProcessLock.AcquireAsync(directory + ".lock", TimeSpan.FromMinutes(20));

bool shouldSave = true;

if (Directory.Exists(directory))
{
    if (File.Exists(completedFilePath))
    {
        shouldSave = false;
    }
    else
    {
        // The directory is left over from a run that did not finish. Recreate it from scratch.
        Directory.Delete(directory, recursive: true);
    }
}

if (shouldSave)
{
    Directory.CreateDirectory(directory);

    foreach (var entry in sdkDownloader.GetAnalyzers())
    {
        var analyzerPath = Path.Combine(directory, entry.Name);

        using var downloadAnalyzerStream = entry.Open();
        using var savingAnalyzerStream = File.OpenWrite(analyzerPath);

        await downloadAnalyzerStream.CopyToAsync(savingAnalyzerStream);
    }

    File.WriteAllText(completedFilePath, "completed");
}

Console.WriteLine(directory);
