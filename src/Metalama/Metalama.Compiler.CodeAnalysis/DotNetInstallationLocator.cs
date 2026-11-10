// Copyright (c) SharpCrafters s.r.o. All rights reserved.
// This project is not open source. Please see the LICENSE.md file in the repository root for details.

// Enumerates the SDKs that the analyzer redirector can fall back to. The primary installation
// is not discovered from the environment: the caller derives it from the path of the analyzer
// being redirected, which comes from the build that sent the compilation. A compiler server is
// shared by builds that may be driven by different .NET installations, so there is no single
// process-wide answer (issue #227).
//
// METALAMA_DOTNET_ROOT, which Metalama.Compiler.targets used to set, is still honored for
// backward compatibility: when it is set, its SDKs are searched after the ones of the
// requesting installation. Note that a compiler server sees the value of the client that
// started it.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Metalama.Compiler;

internal static class DotNetInstallationLocator
{
    internal const string DotnetRootEnvironmentVariable = "METALAMA_DOTNET_ROOT";

    // One Lazy per installation, with ExecutionAndPublication so that the SDK-directory scan of
    // an installation runs exactly once per process — concurrent first-time accesses from
    // multiple analyzers serialize on the same Lazy instead of all doing the disk scan in parallel.
    // Not readonly and volatile: ResetForTests publishes a new instance, and the write must be
    // visible to every subsequent read of the field.
    private static volatile ConcurrentDictionary<string, Lazy<IReadOnlyList<InstalledSdk>>> s_sdksByRoot = CreateCache();

    private static ConcurrentDictionary<string, Lazy<IReadOnlyList<InstalledSdk>>> CreateCache()
        => new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The <c>&lt;dotnet-root&gt;/sdk/&lt;version&gt;/</c> directories to search: those of the
    /// .NET installation at <paramref name="dotnetRoot"/>, then those of the installation named by
    /// <c>METALAMA_DOTNET_ROOT</c> if it is set and different. Each group is sorted by SDK version
    /// descending. The scan of each installation is lazily computed and cached for the process lifetime.
    /// </summary>
    /// <param name="dotnetRoot">A normalized full path, as returned by
    /// <see cref="AnalyzerAssemblyRedirector.TryExtractDotnetRootFromPath"/>, or null when the
    /// requested analyzer is not under an SDK path.</param>
    public static IReadOnlyList<InstalledSdk> GetSdks(string? dotnetRoot)
    {
        var environmentRoot = GetEnvironmentRoot();

        if (environmentRoot == null || string.Equals(environmentRoot, dotnetRoot, StringComparison.OrdinalIgnoreCase))
        {
            return dotnetRoot != null ? GetSdksOfRoot(dotnetRoot) : Array.Empty<InstalledSdk>();
        }

        return dotnetRoot != null
            ? GetSdksOfRoot(dotnetRoot).Concat(GetSdksOfRoot(environmentRoot)).ToArray()
            : GetSdksOfRoot(environmentRoot);
    }

    /// <summary>
    /// Normalizes a .NET installation directory to a full path without trailing separator,
    /// except for a root directory like <c>C:\</c> or <c>/</c>. Returns null if the path is invalid.
    /// </summary>
    public static string? NormalizeRoot(string path)
    {
        try
        {
            var root = Path.GetFullPath(path);
            var pathRootLength = Path.GetPathRoot(root)?.Length ?? 0;

            return root.Length > pathRootLength
                ? root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : root;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? GetEnvironmentRoot()
    {
        var value = Environment.GetEnvironmentVariable(DotnetRootEnvironmentVariable);

        return string.IsNullOrEmpty(value) ? null : NormalizeRoot(value!);
    }

    private static IReadOnlyList<InstalledSdk> GetSdksOfRoot(string root)
        => s_sdksByRoot.GetOrAdd(
                root,
                static r => new Lazy<IReadOnlyList<InstalledSdk>>(() => Discover(r), LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;

    /// <summary>
    /// Discards the cached scans so that a test can change the content of a synthetic .NET
    /// installation.
    /// </summary>
    internal static void ResetForTests() => s_sdksByRoot = CreateCache();

    private static IReadOnlyList<InstalledSdk> Discover(string root)
    {
        var sdkRoot = Path.Combine(root, "sdk");
        if (!Directory.Exists(sdkRoot))
        {
            return Array.Empty<InstalledSdk>();
        }

        var found = new List<InstalledSdk>();
        foreach (var dir in Directory.EnumerateDirectories(sdkRoot))
        {
            var name = Path.GetFileName(dir);
            // Accept prerelease dirs (e.g. "10.0.100-preview.2.25178.4") by stripping the
            // suffix before parsing; the original directory name is what we use on disk.
            var version = AnalyzerAssemblyRedirector.TryParseSdkVersion(name);
            if (version != null)
            {
                found.Add(new InstalledSdk(version, dir, isPrerelease: name.IndexOf('-') >= 0));
            }
        }

        // Sort by numeric version desc, then prefer stable over prerelease when versions
        // are equal so SDK selection stays deterministic regardless of filesystem enumeration
        // order (e.g. 10.0.100 wins over 10.0.100-preview.X).
        found.Sort(static (a, b) =>
        {
            var byVersion = b.Version.CompareTo(a.Version);
            return byVersion != 0 ? byVersion : a.IsPrerelease.CompareTo(b.IsPrerelease);
        });
        return found;
    }
}

internal readonly struct InstalledSdk
{
    public Version Version { get; }
    public string Path { get; }
    public bool IsPrerelease { get; }

    public InstalledSdk(Version version, string path, bool isPrerelease)
    {
        Version = version;
        Path = path;
        IsPrerelease = isPrerelease;
    }
}
