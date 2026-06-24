// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using PostSharp.Engineering.BuildTools.Utilities;

namespace BuildMetalamaCompiler;

internal class RoslynSolution : Solution
{
    public RoslynSolution() : base("Build.ps1")
    {
    }


    public override bool Build(BuildContext context, BuildSettings settings)
    {
        return ExecuteScript(context, settings, "-build");
    }

    private bool ExecuteScript(BuildContext context, BuildSettings settings, string args)
    {
        var msBuildConfiguration =
            context.Product.DependencyDefinition.MSBuildConfiguration[settings.BuildConfiguration];

        var argsBuilder = new StringBuilder();

        argsBuilder.Append(CultureInfo.InvariantCulture, $"-c {msBuildConfiguration}");
        argsBuilder.Append(' ');
        argsBuilder.Append(args);

        if (settings.BuildConfiguration != BuildConfiguration.Debug)
        {
            var revisionNumber = settings.BuildNumber ?? 1;

            // The official build ID is assumed to have format "20yymmdd.r", where R is the revision number of the day.
            // Metalama.Compiler uses the build number as the revision number regardless of the actual date.
            // (See .packages\microsoft.dotnet.arcade.sdk\9.0.0-beta.24416.2\tools\Version.BeforeCommonTargets.targets.)
            var officialBuildId = $"{DateTime.UtcNow:yyyyMMdd}.{revisionNumber}";

            var releaseBranch = context.Product.DependencyDefinition.ReleaseBranch;

            if (releaseBranch == null)
            {
                context.Console.WriteError(
                    "Release branch must be specified when building a public configuration.");
                return false;
            }

            // This parameter is not used by Metalama.Compiler, but it is required by the build script.
            var officialVisualStudioDropAccessToken = "N/A";

            argsBuilder.Append($" -officialBuildId {officialBuildId}");
            argsBuilder.Append(" -officialSkipTests true");
            argsBuilder.Append(" -officialSkipApplyOptimizationData true");
            argsBuilder.Append($" -officialSourceBranchName {releaseBranch}");
            argsBuilder.Append($" -officialVisualStudioDropAccessToken {officialVisualStudioDropAccessToken}");
        }

        // The DOTNET_ROOT_X64 environment variable is used by Arcade.
        var toolOptions = new ToolInvocationOptions
        {
            BlockedEnvironmentVariables = ImmutableArray.Create("MSBuildSDKsPath", "MSBUILD_EXE_PATH"),
            // Retry build when the file is locked by another process.
            Retry = new ToolInvocationRetry(
                new Regex(".+The process cannot access the file.+because it is being used by another process."), 1)
        };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Build with the .NET (Core) MSBuild engine rather than the desktop VS MSBuild.
                // Since Roslyn 5.6, Arcade build tasks (e.g. CompareVersions in eng/targets/Imports.targets)
                // are declared with Runtime="NET", which forces them into an out-of-process .NET task host.
                // The desktop MSBuild shipped with VS Build Tools cannot launch that task host in our build
                // container (there is no co-located .NET MSBuild.dll, and setting DOTNET_HOST_PATH does not
                // help), so the build fails deterministically with MSB4018 "Cannot acquire required number of
                // nodes". The .NET SDK MSBuild hosts these tasks in-process. The Metalama.Compiler.slnf
                // contains no VSIX/desktop-only projects, so it builds cleanly under the .NET engine.
                argsBuilder.Append(" -msbuildEngine dotnet");

                return ToolInvocationHelper.InvokePowershell(
                    context.Console,
                    Path.Combine(context.RepoDirectory, "eng", "build.ps1"),
                    argsBuilder.ToString(),
                    context.RepoDirectory,
                    toolOptions);
            }
            else
            {
                argsBuilder
                    .Replace("-build", "--build")
                    .Replace("-pack", "--pack")
                    .Replace("-restore", "--restore");

                return ToolInvocationHelper.InvokeTool(
                    context.Console,
                    "bash",
                    $"{Path.Combine(context.RepoDirectory, "eng", "build.sh")} {argsBuilder}",
                    context.RepoDirectory,
                    toolOptions);                
            }
        }

    public override bool Pack(BuildContext context, BuildSettings settings)
    {
        return ExecuteScript(context, settings, "-build -pack");
    }

    public override bool Restore(BuildContext context, BuildSettings options)
    {
        return ExecuteScript(context, options, "-restore");
    }

    // We run Metalama's unit tests.
    public override bool Test(BuildContext context, BuildSettings settings)
    {
        var testAll = settings.Properties.ContainsKey("TestAll");

        if (testAll && !string.IsNullOrEmpty(settings.TestsFilter))
        {
            context.Console.WriteError("Tests filter and TestAll property cannot be set at the same time.");
            
            return false;
        }

        var filter = testAll ? "" : settings.TestsFilter ?? context.Product.DefaultTestsFilter;

        var configuration = context.Product.DependencyDefinition.MSBuildConfiguration[settings.BuildConfiguration];

        // We run Metalama's unit tests.
        var testProjectPath = Path.Combine(
            context.RepoDirectory, "src", "Metalama", "Metalama.Compiler.UnitTests", "Metalama.Compiler.UnitTests.csproj");
        var testsBinDirectory = Path.Combine(context.RepoDirectory, "artifacts", "bin", "Metalama.Compiler.UnitTests", configuration);
        var testFileName = "Metalama.Compiler.UnitTests.dll";

        // For non-Debug builds, ExecuteScript passes '-officialSkipTests true', which sets 'buildTests = false'
        // in eng/build.ps1. That skips *compiling* all test projects (not just running them), so the product
        // build never produces Metalama.Compiler.UnitTests. We therefore build that single project explicitly
        // here before running it. Its dependencies have already been built by the product build, so this is
        // incremental. (We use 'dotnet build' so the .NET SDK MSBuild engine is used, as required since Roslyn 5.6.)
        if (!DotNetHelper.Run(context, settings, testProjectPath, "build", addConfigurationFlag: true))
        {
            return false;
        }

        if (!Directory.Exists(testsBinDirectory))
        {
            context.Console.WriteError(
                $"The test output directory '{testsBinDirectory}' does not exist even after building '{testProjectPath}'.");
            return false;
        }

        var testFiles = Directory.GetFiles(testsBinDirectory, testFileName, SearchOption.AllDirectories);
        var actualTestFilesCount = testFiles.Length;

        // Update when the number of target frameworks changes.
        var expectedTestFilesCount = 2;

        if (actualTestFilesCount != expectedTestFilesCount)
        {
            context.Console.WriteError(
                $"{actualTestFilesCount} files found instead of {expectedTestFilesCount} in {testsBinDirectory}.");
            return false;
        }

        var resultsRelativeDirectory = context.Product.TestResultsDirectory;

        var resultsDirectory = Path.Combine(context.RepoDirectory, resultsRelativeDirectory);

        var args =
            $"--filter \"{filter}\" --logger \"trx\" --logger \"console;verbosity=minimal\" --results-directory \"{resultsDirectory}\"";
        var success = true;

        foreach (var testFile in testFiles)
        {
            success &= DotNetHelper.Run(context, settings, testFile, "test", args);
        }

        if (context.IsContinuousIntegrationBuild)
        {
            // Export test result files to TeamCity.
            TeamCityHelper.SendImportDataMessage(
                "vstest",
                Path.Combine(resultsRelativeDirectory, "*.trx").Replace(Path.DirectorySeparatorChar, '/'),
                Path.GetFileName(testFileName),
                false);
        }

        return success;
    }
}
