// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Utilities;

namespace BuildMetalamaCompiler;

/// <summary>
/// Restores, as a post-Prepare step, the MSBuild SDKs listed in the <c>msbuild-sdks</c> section of the
/// root <c>global.json</c> (Arcade, Helix, Traversal, ...) into the NuGet global-packages folder.
/// </summary>
/// <remarks>
/// MSBuild SDKs referenced through <c>global.json</c> are <em>not</em> restored by an ordinary
/// <c>dotnet restore</c> of the solution: they are pulled on demand by the MSBuild SDK resolver during
/// project evaluation. On a build agent whose global-packages cache does not already contain them,
/// Arcade's toolset initialization then fails with <c>"Invalid toolset path: ...\tools\Build.proj"</c>
/// because the SDK package was never placed in the cache. Pre-restoring them here guarantees they are
/// present before the Arcade build script (<c>eng\build.ps1</c>) runs.
/// </remarks>
internal static class RestoreGlobalJsonSdksHelper
{
    public static void OnPrepareCompleted( PrepareCompletedEventArgs args )
    {
        if ( !TryRestore( args.Context ) )
        {
            args.IsFailed = true;
        }
    }

    private static bool TryRestore( BuildContext context )
    {
        var globalJsonPath = Path.Combine( context.RepoDirectory, "global.json" );

        if ( !File.Exists( globalJsonPath ) )
        {
            context.Console.WriteError( $"Cannot restore MSBuild SDKs: '{globalJsonPath}' does not exist." );

            return false;
        }

        // Parse the 'msbuild-sdks' section of global.json.
        var documentOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        using var globalJson = JsonDocument.Parse( File.ReadAllText( globalJsonPath ), documentOptions );

        if ( !globalJson.RootElement.TryGetProperty( "msbuild-sdks", out var sdksElement ) )
        {
            context.Console.WriteMessage( "global.json has no 'msbuild-sdks' section; nothing to restore." );

            return true;
        }

        var sdks = sdksElement.EnumerateObject()
            .Select( property => (Name: property.Name, Version: property.Value.GetString()!) )
            .ToList();

        if ( sdks.Count == 0 )
        {
            return true;
        }

        context.Console.WriteMessage(
            $"Pre-restoring {sdks.Count} MSBuild SDK(s) from global.json: {string.Join( ", ", sdks.Select( s => $"{s.Name} {s.Version}" ) )}." );

        // Generate a throw-away project that downloads the exact pinned versions into the NuGet global-packages
        // folder. It lives outside the repository so that it does not import the repository's Directory.Build.props
        // — that file itself imports the Arcade SDK and computes the product version, which would be a chicken-and-egg
        // dependency on the very SDK we are trying to restore.
        var tempDirectory = Path.Combine( Path.GetTempPath(), "Metalama.Compiler.RestoreSdks", Guid.NewGuid().ToString( "N" ) );
        Directory.CreateDirectory( tempDirectory );

        try
        {
            var projectBuilder = new StringBuilder();
            projectBuilder.AppendLine( "<Project Sdk=\"Microsoft.NET.Sdk\">" );
            projectBuilder.AppendLine( "  <PropertyGroup>" );
            projectBuilder.AppendLine( "    <TargetFramework>net10.0</TargetFramework>" );
            projectBuilder.AppendLine( "    <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>" );
            projectBuilder.AppendLine( "    <EnableDefaultItems>false</EnableDefaultItems>" );
            projectBuilder.AppendLine( "    <ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>" );
            projectBuilder.AppendLine( "    <ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>" );
            projectBuilder.AppendLine( "  </PropertyGroup>" );
            projectBuilder.AppendLine( "  <ItemGroup>" );

            foreach ( var sdk in sdks )
            {
                // PackageDownload requires an exact-version range in brackets and downloads the full package
                // (including the 'tools' folder Arcade needs) into the global-packages folder.
                projectBuilder.AppendLine( $"    <PackageDownload Include=\"{sdk.Name}\" Version=\"[{sdk.Version}]\" />" );
            }

            projectBuilder.AppendLine( "  </ItemGroup>" );
            projectBuilder.AppendLine( "</Project>" );

            var projectPath = Path.Combine( tempDirectory, "RestoreSdks.csproj" );
            File.WriteAllText( projectPath, projectBuilder.ToString() );

            // Use the repository's NuGet.config so the SDKs are resolved from the same feeds as the build, and write
            // to the same global-packages folder (inherited via the NUGET_PACKAGES environment variable).
            var nuGetConfigPath = Path.Combine( context.RepoDirectory, "nuget.config" );
            var arguments = $"restore \"{projectPath}\" --configfile \"{nuGetConfigPath}\" --verbosity minimal";

            if ( !ToolInvocationHelper.InvokeTool( context.Console, "dotnet", arguments, tempDirectory ) )
            {
                context.Console.WriteError( "Failed to restore the MSBuild SDKs declared in global.json." );

                return false;
            }

            return true;
        }
        finally
        {
            try
            {
                Directory.Delete( tempDirectory, recursive: true );
            }
            catch ( IOException )
            {
                // Ignore clean-up failures; the directory is under the temporary folder.
            }
        }
    }
}
