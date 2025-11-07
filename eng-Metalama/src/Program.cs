using System.Collections.Generic;
using System.IO;
using BuildMetalamaCompiler;
using BuildMetalamaCompiler.NuGetDependencies;
using PostSharp.Engineering.BuildTools;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Docker;
using PostSharp.Engineering.BuildTools.Tools.NuGet;
using Spectre.Console.Cli;
using MetalamaDependencies = PostSharp.Engineering.BuildTools.Dependencies.Definitions.MetalamaDependencies.V2026_0;

var product = new Product(MetalamaDependencies.MetalamaCompiler)
{
    OverriddenBuildAgentRequirements = new ContainerRequirements(ContainerHostKind.Windows)
    {
        Components =
        [
            // Must match global.json.
            new DotNetComponent("10.0.100-rc.1.25451.107", DotNetComponentKind.Sdk),
            new VisualStudioBuildToolsComponent(
                VisualStudioBuildToolsComponentVersion.v17_14_15,
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
    AdditionalDirectoriesToClean = ["artifacts"],
    Solutions = [new RoslynSolution()],
    PublicArtifacts =
        Pattern.Create("Metalama.Compiler.$(PackageVersion).nupkg",
            "Metalama.Compiler.Sdk.$(PackageVersion).nupkg"),
    PrivateArtifacts =
        Pattern.Create(
            "Metalama.Roslyn.CodeAnalysis.Common.$(PackageVersion).nupkg",
            "Metalama.Roslyn.CodeAnalysis.CSharp.$(PackageVersion).nupkg",
            "Metalama.Roslyn.CodeAnalysis.CSharp.Workspaces.$(PackageVersion).nupkg",
            "Metalama.Roslyn.CodeAnalysis.Workspaces.Common.$(PackageVersion).nupkg",
            "Metalama.Roslyn.CodeAnalysis.Workspaces.MSBuild.$(PackageVersion).nupkg"),

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

product.BuildCompleted += OnBuildCompleted;


var app = new EngineeringApp(product);

app.Configure(delegate(IConfigurator root)
{
    root.AddCommand<PushNuGetDependenciesCommand>("push-nuget-dependencies")
        .WithData(new BaseCommandData(product))
        .WithDescription(
            "Pushes NuGet dependencies not coming from NuGet.org to Azure Artifacts repository. See See docs-Metalama/Merging.md for details.");
});

return app.Run(args);

static void OnBuildCompleted( BuildCompletedEventArgs args )
{
    // Rename the packages as a post-build step.
    args.Context.Console.WriteHeading( "Renaming packages" );

    var success = RenamePackagesCommand.Execute( args.Context.Console, new RenamePackageCommandSettings { Directory = args.PrivateArtifactsDirectory } );

    if ( success )
    {
        // Delete original packages (those non-renamed) so they don't get uploaded.
        foreach ( var file in Directory.GetFiles( args.PrivateArtifactsDirectory, "Microsoft.*.nupkg" ) )
        {
            File.Delete( file );
        }

        args.Context.Console.WriteSuccess( "Renaming packages was successful." );
    }

    args.IsFailed = !success;
}
