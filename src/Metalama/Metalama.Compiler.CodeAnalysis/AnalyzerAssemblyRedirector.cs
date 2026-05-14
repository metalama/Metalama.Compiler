// Copyright (c) SharpCrafters s.r.o. All rights reserved.
// This project is not open source. Please see the LICENSE.md file in the repository root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Metalama.Compiler;

/// <summary>
/// Resolves an SDK-shipped analyzer DLL whose Microsoft.CodeAnalysis dependency is
/// newer than the Roslyn bundled with Metalama Compiler to a compatible older copy
/// found in a side-by-side installed .NET SDK on the build host.
///
/// Replaces the previous design that bundled SDK analyzers in the package's
/// <c>sdkAnalyzers/</c> folder. With modern .NET 10 SDKs shipping composite R2R
/// analyzer DLLs (no IL fallback), bundling them in any form is no longer viable;
/// the only place an IL copy reliably exists is inside another installed SDK.
/// See issue #180.
/// </summary>
internal static class AnalyzerAssemblyRedirector
{
    private static readonly ConcurrentDictionary<string, string?> s_cache
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Finds an installed-SDK copy of the analyzer at <paramref name="originalPath"/>
    /// whose Microsoft.CodeAnalysis reference is &lt;= <paramref name="maxRoslynVersion"/>.
    /// Returns the resolved path, or null if no compatible copy is installed.
    /// </summary>
    /// <remarks>
    /// On a successful resolution, all sibling DLLs in the same SDK directory are
    /// pre-populated in the cache. This ensures that when a transitive reference of the
    /// redirected analyzer is later resolved, it comes from the same SDK directory —
    /// preserving the ABI-coordinated pair the .NET team shipped (avoids
    /// CS8785 MissingMethodException across mismatched analyzer versions).
    /// </remarks>
    public static string? FindCompatibleAnalyzer(string originalPath, Version maxRoslynVersion)
    {
        var fileName = Path.GetFileName(originalPath);
        if (s_cache.TryGetValue(fileName, out var cached))
        {
            return cached;
        }

        var resolved = FindUncached(fileName, maxRoslynVersion);
        s_cache[fileName] = resolved;

        if (resolved != null)
        {
            // Pre-populate the cache with sibling DLLs from the same directory so transitive
            // references resolve to the same SDK / coordinated build.
            var siblingDir = Path.GetDirectoryName(resolved);
            if (siblingDir != null)
            {
                try
                {
                    foreach (var sibling in Directory.EnumerateFiles(siblingDir, "*.dll"))
                    {
                        var siblingName = Path.GetFileName(sibling);
                        // Don't overwrite an existing entry — first hit wins so cross-analyzer
                        // resolution stays deterministic.
                        s_cache.TryAdd(siblingName, sibling);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        return resolved;
    }

    private static string? FindUncached(string fileName, Version maxRoslynVersion)
    {
        foreach (var sdk in DotNetInstallationLocator.Sdks)
        {
            var sdksRoot = Path.Combine(sdk.Path, "Sdks");
            if (!Directory.Exists(sdksRoot))
            {
                continue;
            }

            IEnumerable<string> candidates;
            try
            {
                candidates = Directory.EnumerateFiles(sdksRoot, fileName, SearchOption.AllDirectories);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                if (IsCompatibleWith(candidate, maxRoslynVersion))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static bool IsCompatibleWith(string filePath, Version maxRoslynVersion)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
            {
                return false;
            }

            var md = pe.GetMetadataReader();
            foreach (var refHandle in md.AssemblyReferences)
            {
                var reference = md.GetAssemblyReference(refHandle);
                if (md.GetString(reference.Name) == "Microsoft.CodeAnalysis")
                {
                    return reference.Version <= maxRoslynVersion;
                }
            }

            // No reference to Microsoft.CodeAnalysis → not version-constrained → compatible.
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns a previously-cached redirect path for the given analyzer file (typically
    /// pre-populated as a sibling of a previously-redirected analyzer). Does not trigger
    /// a search. Returns null if no cache entry exists OR if the cache entry is the
    /// negative "no compatible SDK" result.
    /// </summary>
    public static string? TryGetCachedPath(string originalPath)
    {
        var fileName = Path.GetFileName(originalPath);
        return s_cache.TryGetValue(fileName, out var cached) ? cached : null;
    }

    /// <summary>Snapshot of installed SDK versions for inclusion in the no-found error.</summary>
    public static IReadOnlyList<Version> EnumerateInstalledSdkVersions()
        => DotNetInstallationLocator.Sdks.Select(s => s.Version).ToArray();

    /// <summary>
    /// Extracts the SDK version from a path like
    /// <c>…/sdk/10.0.300/Sdks/Microsoft.NET.Sdk.Razor/source-generators/foo.dll</c>.
    /// Returns null if no <c>/sdk/&lt;version&gt;/</c> segment is found.
    /// </summary>
    public static string? TryExtractSdkVersionFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var parts = path!.Replace('\\', '/').Split('/');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (string.Equals(parts[i], "sdk", StringComparison.OrdinalIgnoreCase)
                && Version.TryParse(parts[i + 1], out _))
            {
                return parts[i + 1];
            }
        }

        return null;
    }
}
