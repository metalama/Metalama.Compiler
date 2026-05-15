// Copyright (c) SharpCrafters s.r.o. All rights reserved.
// This project is not open source. Please see the LICENSE.md file in the repository root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Roslyn.Utilities;

namespace Metalama.Compiler;

/// <summary>
/// Resolves an SDK-shipped analyzer DLL whose Microsoft.CodeAnalysis dependency is
/// newer than the Roslyn bundled with Metalama Compiler to a compatible older copy.
/// The copy is found in two places, tried in order:
/// <list type="number">
///   <item>The package's <c>sdkAnalyzers/</c> bundle (built from an LKG SDK on the
///         Metalama.Compiler build host). Used when a PE inspection determines that
///         the bundled DLL is loadable on the current platform.</item>
///   <item>An installed .NET SDK on the build host. Used as a fallback when the
///         bundle is missing or not loadable (e.g. running on Linux against a bundle
///         of Windows-x64 R2R DLLs).</item>
/// </list>
/// See issue #180.
/// </summary>
internal static class AnalyzerAssemblyRedirector
{
    // Subdirectories under <sdk>/Sdks/<sdk-name>/ where analyzer / source-generator DLLs
    // live. Limits the recursive search and skips unrelated SDK content (tools/, build/, etc.)
    // to keep the scan cost low on machines with many SDKs.
    private static readonly string[] s_analyzerSubdirNames = { "analyzers", "source-generators" };

    // Cache is keyed by analyzer file name. The first analyzer that triggers a redirect
    // pre-populates entries for every DLL in the source directory (bundle or SDK), so that
    // transitive references (which share the file name) resolve to the same source and
    // preserve the binary-compatible pair the .NET team coordinated.
    //
    // Collisions are possible if two unrelated analyzers share a file name; in practice
    // SDK-shipped analyzers have unique names within the SDK layout, and only those go
    // through this code path.
    private static readonly ConcurrentDictionary<string, string?> s_cache
        = new(StringComparer.OrdinalIgnoreCase);

    // Bundle of analyzer DLLs shipped inside the Metalama.Compiler nupkg. Lazy because
    // the path is computed from the loaded assembly location, which we'd rather not
    // touch at static-init time on the netstandard2.0 build.
    private static readonly Lazy<string?> s_bundledAnalyzersDirectory =
        new(GetBundledAnalyzersDirectory, LazyThreadSafetyMode.ExecutionAndPublication);

    // The SDK version the bundle was built against; written to Microsoft.CodeAnalysis.dll
    // as an AssemblyMetadata attribute by the AddDotnetSdkAssemblyAttribute MSBuild target.
    // Used to populate the {redirectedSdkVersion} arg of LAMA0617 when the bundle is used.
    private static readonly Lazy<string?> s_bundledSdkVersion =
        new(GetBundledSdkVersion, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Pre-resolves every SDK analyzer in <paramref name="analyzerReferences"/> that needs
    /// the installed-SDK fallback (issue #180). Populates the redirector's path cache so
    /// the regular per-reference resolution gets cache hits regardless of MSBuild's analyzer
    /// ordering, and emits the LAMA0617 / LAMA0625 diagnostics here so the per-reference
    /// loop doesn't need to repeat the metadata read.
    /// </summary>
    public static void PreResolve(
        ImmutableArray<CommandLineAnalyzerReference> analyzerReferences,
        string? baseDirectory,
        ImmutableArray<string> referencePaths,
        Version maxRoslynVersion,
        List<DiagnosticInfo> diagnostics)
    {
        foreach (var reference in analyzerReferences.Distinct())
        {
            var resolvedPath = FileUtilities.ResolveRelativePath(
                reference.FilePath,
                basePath: null,
                baseDirectory: baseDirectory,
                searchPaths: referencePaths,
                fileExists: File.Exists);

            if (resolvedPath == null)
            {
                continue;
            }

            // Skip references that aren't from an installed SDK. The redirect mechanism only
            // applies to SDK-shipped analyzers (path contains /sdk/<version>/); third-party
            // analyzers fall back to Roslyn's normal handling.
            if (TryExtractSdkVersionFromPath(resolvedPath) == null)
            {
                continue;
            }

            // Read the analyzer's referenced Microsoft.CodeAnalysis version without
            // loading the assembly. Failures here are real and must surface — a malformed
            // or inaccessible analyzer DLL is the kind of bug we want to fail loudly on.
            using var assembly = AssemblyMetadata.CreateFromFile(resolvedPath);
            var referencedRoslynVersion = assembly.GetModules().First().Module.ReferencedAssemblies
                .FirstOrDefault(a => a.Name == "Microsoft.CodeAnalysis")?.Version;

            // The redirect only fires when the analyzer references a Roslyn newer than what
            // Metalama Compiler ships. The Major >= 2023 check skips test-only year-based
            // Metalama Roslyn versions (matches the per-reference logic below).
            if (referencedRoslynVersion == null
                || referencedRoslynVersion.Major >= 2023
                || referencedRoslynVersion <= maxRoslynVersion)
            {
                continue;
            }

            var requestedSdkVersion = TryExtractSdkVersionFromPath(resolvedPath) ?? "(unknown)";
            var redirectedPath = FindCompatibleAnalyzer(resolvedPath, maxRoslynVersion);
            var fileName = Path.GetFileName(resolvedPath);

            if (redirectedPath != null)
            {
                var redirectedSdkVersion = GetRedirectedAnalyzerSdkVersion(redirectedPath) ?? "(unknown)";
                diagnostics.Add(new DiagnosticInfo(
                    MetalamaCompilerMessageProvider.Instance,
                    (int)MetalamaErrorCode.WRN_AnalyzerAssembliesRedirected,
                    fileName,
                    requestedSdkVersion,
                    referencedRoslynVersion.ToString(),
                    maxRoslynVersion.ToString(),
                    redirectedSdkVersion));
            }
            else
            {
                var installedSdks = EnumerateInstalledSdkVersions();
                var installedSdksText = installedSdks.Count == 0
                    ? "(none)"
                    : string.Join(", ", installedSdks.Select(v => v.ToString()));
                diagnostics.Add(new DiagnosticInfo(
                    MetalamaCompilerMessageProvider.Instance,
                    (int)MetalamaErrorCode.ERR_NoCompatibleSdkForAnalyzer,
                    fileName,
                    requestedSdkVersion,
                    referencedRoslynVersion.ToString(),
                    maxRoslynVersion.ToString(),
                    installedSdksText));
            }
        }
    }

    /// <summary>
    /// Finds an installed-SDK copy of the analyzer at <paramref name="originalPath"/>
    /// whose Microsoft.CodeAnalysis reference is &lt;= <paramref name="maxRoslynVersion"/>.
    /// Returns the resolved path, or null if no compatible copy is installed.
    /// </summary>
    /// <remarks>
    /// On a successful resolution, all sibling DLLs in the same SDK directory are
    /// pre-populated in the cache. This ensures that when a transitive reference of the
    /// redirected analyzer is later resolved, it comes from the same SDK directory —
    /// preserving the binary-compatible pair the .NET team shipped (avoids
    /// CS8785 MissingMethodException across mismatched analyzer versions).
    /// </remarks>
    public static string? FindCompatibleAnalyzer(string originalPath, Version maxRoslynVersion)
    {
        var fileName = Path.GetFileName(originalPath);
        if (s_cache.TryGetValue(fileName, out var cached))
        {
            return cached;
        }

        // Bundle takes precedence over installed SDKs: when the bundle ships a
        // platform-loadable copy of the requested analyzer, that's the version we
        // know to be binary-compatible with our Roslyn (Metalama.Compiler built it).
        // The SDK scan is the fallback for platforms where the bundle isn't loadable
        // (Linux/macOS against Windows-x64 R2R) or when the bundle doesn't contain
        // the requested file.
        var resolved = TryGetBundledRedirect(fileName) ?? FindUncached(fileName, maxRoslynVersion);
        s_cache[fileName] = resolved;

        if (resolved != null)
        {
            // Pre-populate the cache with sibling DLLs from the same directory so transitive
            // references resolve to the same SDK / coordinated build.
            var siblingDir = Path.GetDirectoryName(resolved);
            if (siblingDir != null)
            {
                foreach (var sibling in Directory.EnumerateFiles(siblingDir, "*.dll"))
                {
                    var siblingName = Path.GetFileName(sibling);
                    // Don't overwrite an existing entry — first hit wins so cross-analyzer
                    // resolution stays deterministic.
                    s_cache.TryAdd(siblingName, sibling);
                }
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

            foreach (var candidate in EnumerateAnalyzerCandidates(sdksRoot, fileName))
            {
                if (IsCompatibleWith(candidate, maxRoslynVersion))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateAnalyzerCandidates(string sdksRoot, string fileName)
    {
        // Narrow the scan: only look in <sdk>/Sdks/<sdk-name>/{analyzers,source-generators}/.
        // Avoids walking the full <sdk>/Sdks/** tree which contains many unrelated files
        // (tools/, build/, *.props, *.targets, …).
        foreach (var sdkSubdir in Directory.EnumerateDirectories(sdksRoot))
        {
            foreach (var analyzerSubdirName in s_analyzerSubdirNames)
            {
                var analyzerSubdir = Path.Combine(sdkSubdir, analyzerSubdirName);
                if (!Directory.Exists(analyzerSubdir))
                {
                    continue;
                }

                foreach (var hit in Directory.EnumerateFiles(analyzerSubdir, fileName, SearchOption.AllDirectories))
                {
                    yield return hit;
                }
            }
        }
    }

    private static bool IsCompatibleWith(string filePath, Version maxRoslynVersion)
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

    private static string? TryGetBundledRedirect(string fileName)
    {
        if (s_bundledAnalyzersDirectory.Value is not { } dir)
        {
            return null;
        }

        var path = Path.Combine(dir, fileName);
        return File.Exists(path) && IsLoadableOnCurrentPlatform(path) ? path : null;
    }

    /// <summary>
    /// Decides whether the bundled DLL at <paramref name="filePath"/> can be loaded by
    /// the running compiler process. Pure-IL assemblies are portable; assemblies with
    /// native code (R2R composite or mixed-mode) are platform-specific and must match
    /// both OS and architecture. The Metalama.Compiler bundle is built on Windows, so
    /// native code in it is always Windows-targeting.
    /// </summary>
    private static bool IsLoadableOnCurrentPlatform(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
        {
            return false;
        }

        var cor = pe.PEHeaders.CorHeader;
        if (cor == null)
        {
            return false;
        }

        // Pure IL assemblies are platform-portable.
        if ((cor.Flags & CorFlags.ILOnly) != 0)
        {
            return true;
        }

        // Native code present. The bundle's native code is Windows-targeting; require
        // both an OS match and a Machine match.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        return MachineMatchesCurrentArchitecture(pe.PEHeaders.CoffHeader.Machine);
    }

    private static bool MachineMatchesCurrentArchitecture(Machine peMachine)
        => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => peMachine == Machine.Amd64,
            Architecture.X86 => peMachine == Machine.I386,
            Architecture.Arm64 => peMachine == Machine.Arm64,
            Architecture.Arm => peMachine == Machine.Arm,
            _ => false
        };

    private static string? GetBundledAnalyzersDirectory()
    {
        // Microsoft.CodeAnalysis.dll lives at:
        //   .NET Core:      tasks/net8.0/bincore/Microsoft.CodeAnalysis.dll
        //   .NET Framework: tasks/net472/Microsoft.CodeAnalysis.dll
        // sdkAnalyzers/ is a sibling of tasks/, so walk up 3 levels for bincore,
        // 2 levels otherwise.
        var assemblyDirectory = Path.GetDirectoryName(typeof(AnalyzerAssemblyRedirector).Assembly.Location);
        if (string.IsNullOrEmpty(assemblyDirectory))
        {
            return null;
        }

        var pathToRoot = assemblyDirectory!.Contains("bincore") ? "../../.." : "../..";
        var dir = FileUtilities.TryNormalizeAbsolutePath(Path.Combine(assemblyDirectory, pathToRoot, "sdkAnalyzers"))
                  ?? Path.GetFullPath(Path.Combine(assemblyDirectory, pathToRoot, "sdkAnalyzers"));

        return Directory.Exists(dir) ? dir : null;
    }

    private static string? GetBundledSdkVersion()
        => typeof(AnalyzerAssemblyRedirector).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "DotnetSdkVersion")?.Value;

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
    /// Returns the SDK version that <paramref name="redirectedPath"/> belongs to. For a
    /// path inside the bundle, returns the version embedded in Microsoft.CodeAnalysis.dll
    /// at build time (<c>DotnetSdkVersion</c>). For a path inside an installed SDK, parses
    /// the version from the <c>/sdk/&lt;version&gt;/</c> segment. Returns null when neither
    /// is applicable.
    /// </summary>
    public static string? GetRedirectedAnalyzerSdkVersion(string redirectedPath)
    {
        if (s_bundledAnalyzersDirectory.Value is { } bundle
            && redirectedPath.StartsWith(bundle, StringComparison.OrdinalIgnoreCase))
        {
            return s_bundledSdkVersion.Value;
        }

        return TryExtractSdkVersionFromPath(redirectedPath);
    }

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
                && TryParseSdkVersion(parts[i + 1]) != null)
            {
                return parts[i + 1];
            }
        }

        return null;
    }

    /// <summary>
    /// Parses an installed-SDK directory name as a <see cref="Version"/>, accepting the
    /// prerelease shapes that show up in real-world SDK installs (e.g.
    /// <c>10.0.100-preview.2.25178.4</c>). The numeric prefix before the first <c>-</c>
    /// is what we use for ordering; the prerelease suffix is preserved by the caller
    /// for path-name purposes.
    /// </summary>
    internal static Version? TryParseSdkVersion(string dirName)
    {
        if (string.IsNullOrEmpty(dirName))
        {
            return null;
        }

        var dash = dirName.IndexOf('-');
        var numeric = dash < 0 ? dirName : dirName.Substring(0, dash);
        return Version.TryParse(numeric, out var v) ? v : null;
    }
}
