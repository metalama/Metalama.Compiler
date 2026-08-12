namespace DownloadNetSdkAnalyzers;

/// <summary>
/// An exclusive lock between processes, held for as long as the returned object is alive.
/// </summary>
/// <remarks>
/// Several instances of this tool run at the same time: both Microsoft.CodeAnalysis.csproj and
/// Metalama.Compiler.Package.csproj invoke it from targets that run once per TargetFramework, and MSBuild
/// builds the inner builds in parallel. They share the release cache in the tool directory and the analyzer
/// directory under the temporary folder, so those have to be guarded.
///
/// A lock file is used rather than a <see cref="Mutex"/>, because a mutex belongs to the thread that took it
/// and the callers await in between.
/// </remarks>
internal static class CrossProcessLock
{
    public static async Task<FileStream> AcquireAsync(string path, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow - started < timeout)
            {
                // Another instance holds the lock. It may be downloading an SDK archive, which takes a while.
                await Task.Delay(500);
            }
        }
    }
}
