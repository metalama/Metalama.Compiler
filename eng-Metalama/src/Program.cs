using System.Collections.Generic;
using BuildMetalamaCompiler;
using PostSharp.Engineering.BuildTools;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Arguments;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Triggers;
using PostSharp.Engineering.BuildTools.Docker;
using MetalamaDependencies = PostSharp.Engineering.BuildTools.Dependencies.Definitions.MetalamaDependencies.V2027_0;

var product = new Product(MetalamaDependencies.MetalamaCompiler)
{
    OverriddenBuildAgentRequirements = new ContainerRequirements(ContainerHostKind.Windows)
    {
        Components =
        [
            // Must match global.json.
            new DotNetComponent("10.0.301", DotNetComponentKind.Sdk),

            new VisualStudioBuildToolsComponent(
                VisualStudioBuildToolsComponentVersion.v18_9_2,
            [
                "Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools",
                "Microsoft.VisualStudio.Workload.NetCoreBuildTools",
                "Microsoft.VisualStudio.Workload.MSBuildTools",
                "Microsoft.Net.Component.4.7.2.TargetingPack",
                "Microsoft.Net.Component.4.7.2.SDK",
                "Microsoft.NetCore.Component.SDK"
            ])
        ]
    },
    VersionsFilePath = "eng\\Versions.props",
    GenerateArcadeProperties = true,
    AdditionalCiBuildConfigurations =
    [
        // Nightly check for a due Roslyn merge. Almost every run ends at the first step with "no merge due";
        // it only acts when a GA .NET SDK ships a Roslyn newer than the one bundled here, which is a few
        // times a year. The procedure lives in eng-Metalama/prompts/RoslynMergeCheck.md rather than in this
        // argument, so that it is reviewable and does not have to survive three levels of quoting.
        new PowershellAdditionalCiBuildConfiguration(
            "RoslynMergeCheck",
            "Nightly Roslyn Merge Check",
            "DockerBuild.ps1",
            "-Claude -NoMcp \"Follow eng-Metalama/prompts/RoslynMergeCheck.md *STRICTLY*, and respect CLAUDE.md.\"")
        {
            Dockerfile = @".\eng-Metalama\docker\claude.Dockerfile",

            // The agent opens pull requests under its own GitHub app, not under the build system's. The token
            // goes to CLAUDE_GITHUB_TOKEN because DockerBuild.ps1 forwards a host variable into the container
            // only when it carries a CLAUDE_ prefix, and it arrives inside as GITHUB_TOKEN.
            GitHubAppToken = new GitHubAppTokenOverride(GitHubAppConnections.MetalamaAgent, "env.CLAUDE_GITHUB_TOKEN"),

            // 03:00, after the nightly builds. withPendingChangesOnly must be false: the trigger for this job
            // is a release on the .NET side, which produces no commit in this repository.
            BuildTriggers = [new NightlyBuildTrigger(3, withPendingChangesOnly: false)]
        }
    ],
    AdditionalDirectoriesToClean = ["artifacts"],
    Solutions =
    [
        new RoslynSolution(),

        // Standalone scenarios covering behaviors that only manifest under the .NET Framework-hosted
        // compiler, i.e. AnalyzerAssemblyLoader.Desktop.cs, which is '#if !NETCOREAPP' and is therefore
        // unreachable from anything built with 'dotnet'. Skipped on non-Windows.
        new ManyMSBuildSolutions(@"src\Metalama\tests\Standalone") { IsTestOnly = true }
    ],
    PublicArtifacts =
        Pattern.Create("Metalama.Compiler.$(PackageVersion).nupkg",
            "Metalama.Compiler.Sdk.$(PackageVersion).nupkg"),
    SupportedProperties =
        new Dictionary<string, string>
        {
            ["TestAll"] =
                "Supported by the 'test' command. Run all tests instead of just Metalama's unit tests."
        },
    ExportedProperties = { { @"eng\Versions.props", ["RoslynVersion"] } },
    KeepEditorConfig = true,
    Configurations =
        Product.DefaultConfigurations.WithValue(BuildConfiguration.Release,
            c => c with { ExportsToTeamCityBuild = true }),
    DefaultTestsFilter = "Category!=OuterLoop"
};

var app = new EngineeringApp(product);

return app.Run(args);
