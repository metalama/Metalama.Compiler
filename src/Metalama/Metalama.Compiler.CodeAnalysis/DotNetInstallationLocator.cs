// Copyright (c) SharpCrafters s.r.o. All rights reserved.
// This project is not open source. Please see the LICENSE.md file in the repository root for details.

// Adapted from Metalama.Backstage.Infrastructure.PlatformInfo, simplified for the
// compiler scenario (no design-time host concerns, no Rider, no architecture-specific
// DOTNET_ROOT env vars). MSBuild already knows the dotnet root of the SDK driving the
// build and hints it to us via METALAMA_DOTNET_ROOT; the fallbacks cover unusual cases.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Metalama.Compiler;

internal static class DotNetInstallationLocator
{
    // Lazy with ExecutionAndPublication so the SDK-directory scan runs exactly once
    // per process — concurrent first-time accesses from multiple analyzers serialize
    // on the same Lazy instead of all doing the disk scan in parallel.
    private static Lazy<IReadOnlyList<InstalledSdk>> s_sdks = CreateLazy();

    private static Lazy<IReadOnlyList<InstalledSdk>> CreateLazy()
        => new(Discover, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// All <c>&lt;dotnet-root&gt;/sdk/&lt;version&gt;/</c> directories the compiler can see,
    /// sorted by SDK version descending. Lazily computed and cached for the process
    /// lifetime.
    /// </summary>
    public static IReadOnlyList<InstalledSdk> Sdks => s_sdks.Value;

    /// <summary>
    /// Discards the cached scan so that a test can point the locator at a synthetic .NET
    /// installation through the <c>METALAMA_DOTNET_ROOT</c> environment variable.
    /// </summary>
    internal static void ResetForTests() => s_sdks = CreateLazy();

    private static IReadOnlyList<InstalledSdk> Discover()
    {
        var root = ResolveDotnetRoot();
        if (root == null)
        {
            return Array.Empty<InstalledSdk>();
        }

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

    private static string? ResolveDotnetRoot()
    {
        // 1. MSBuild-provided hint set by Metalama.Compiler.targets.
        var hint = Environment.GetEnvironmentVariable("METALAMA_DOTNET_ROOT");
        if (!string.IsNullOrEmpty(hint) && Directory.Exists(Path.Combine(hint, "sdk")))
        {
            return hint;
        }

        // 2. Fallback: the dotnet host that launched MSBuild.
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(hostPath))
        {
            var dir = Path.GetDirectoryName(hostPath);
            if (dir != null && Directory.Exists(Path.Combine(dir, "sdk")))
            {
                return dir;
            }
        }

        return null;
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
