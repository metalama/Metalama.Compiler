// Copyright (c) SharpCrafters s.r.o. All rights reserved.
// This project is not open source. Please see the LICENSE.md file in the repository root for details.

// Adapted from Metalama.Backstage.Infrastructure.PlatformInfo, simplified for the
// compiler scenario (no design-time host concerns, no Rider, no architecture-specific
// DOTNET_ROOT env vars). MSBuild already knows the dotnet root of the SDK driving the
// build and hints it to us via METALAMA_DOTNET_ROOT; the fallbacks cover unusual cases.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Metalama.Compiler;

internal static class DotNetInstallationLocator
{
    private static IReadOnlyList<InstalledSdk>? _sdks;

    /// <summary>
    /// All <c>&lt;dotnet-root&gt;/sdk/&lt;version&gt;/</c> directories the compiler can see,
    /// sorted by SDK version descending. Lazily computed and cached for the process
    /// lifetime.
    /// </summary>
    public static IReadOnlyList<InstalledSdk> Sdks
    {
        get
        {
            var sdks = Volatile.Read(ref _sdks);
            if (sdks != null)
            {
                return sdks;
            }

            sdks = Discover();
            Interlocked.CompareExchange(ref _sdks, sdks, null);
            return Volatile.Read(ref _sdks)!;
        }
    }

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
            if (Version.TryParse(name, out var version))
            {
                found.Add(new InstalledSdk(version, dir));
            }
        }

        found.Sort(static (a, b) => b.Version.CompareTo(a.Version));
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

    public InstalledSdk(Version version, string path)
    {
        Version = version;
        Path = path;
    }
}
