// Copyright (c) SharpCrafters s.r.o. All rights reserved.
// This project is not open source. Please see the LICENSE.md file in the repository root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Metalama.Compiler.UnitTests
{
    // The redirector and the .NET installation locator both cache in process-wide statics, and these
    // tests drive them through the METALAMA_DOTNET_ROOT environment variable, so they must not run
    // next to anything else that touches the same state.
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class AnalyzerRedirectCollection
    {
        public const string Name = "AnalyzerRedirect";
    }

    /// <summary>
    /// Tests for the substitution of an SDK-shipped analyzer whose Microsoft.CodeAnalysis reference is
    /// newer than the Roslyn bundled with Metalama Compiler. See issues #180 and #208.
    /// </summary>
    [Collection(AnalyzerRedirectCollection.Name)]
    public sealed class AnalyzerRedirectTests : IDisposable
    {
        private const string _newSdkVersion = "9999.0.100";
        private const string _oldSdkVersion = "1.2.300";

        // Greater than any Roslyn version that Metalama Compiler can bundle, and below the year-based
        // versions that the redirector treats as test-only Metalama builds.
        private const string _tooNewRoslynVersion = "2000.0.0.0";

        private const string _compatibleRoslynVersion = "1.0.0.0";

        private readonly string _rootDirectory;
        private readonly string? _previousDotNetRoot;

        public AnalyzerRedirectTests()
        {
            this._rootDirectory = Path.Combine(Path.GetTempPath(), "Metalama.Compiler.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this._rootDirectory);

            this._previousDotNetRoot = Environment.GetEnvironmentVariable("METALAMA_DOTNET_ROOT");
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("METALAMA_DOTNET_ROOT", this._previousDotNetRoot);
            DotNetInstallationLocator.ResetForTests();
            AnalyzerAssemblyRedirector.ResetForTests();

            try
            {
                Directory.Delete(this._rootDirectory, recursive: true);
            }
            catch (IOException)
            {
                // A file still held open by a metadata reader must not fail the test.
            }
        }

        /// <summary>
        /// The substituted analyzer must be able to load the assemblies it depends on. A newer .NET SDK
        /// does not necessarily ship the same set of files next to the analyzer as the older SDK the
        /// substitution comes from, so a dependency of the substituted analyzer is not necessarily among
        /// the analyzer references passed on the command line. The redirect must therefore register the
        /// assemblies of the substitution source with the analyzer loader. See issue #208.
        /// </summary>
        /// <param name="preResolve">
        /// Whether the pre-pass of <see cref="AnalyzerAssemblyRedirector.PreResolve"/> runs first, as it
        /// does in a command-line compilation. Both paths must carry the dependencies.
        /// </param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RedirectRegistersTheDependenciesOfTheSubstitutedAnalyzer(bool preResolve)
        {
            var newAnalyzerDirectory = this.CreateAnalyzerDirectory(_newSdkVersion);
            var oldAnalyzerDirectory = this.CreateAnalyzerDirectory(_oldSdkVersion);

            // The dependency is shipped only by the old SDK. This is the situation reported in #208: the
            // analyzer of the new SDK does not need this assembly, but the older copy that is substituted
            // for it does.
            var oldDependencyPath = Path.Combine(oldAnalyzerDirectory, "TestDependency.dll");
            EmitAssembly(oldDependencyPath, "TestDependency", "public class DependencyMarker {}");

            var newAnalyzerPath = Path.Combine(newAnalyzerDirectory, "TestAnalyzer.dll");
            this.EmitAnalyzer(newAnalyzerPath, _tooNewRoslynVersion, oldDependencyPath);

            var oldAnalyzerPath = Path.Combine(oldAnalyzerDirectory, "TestAnalyzer.dll");
            this.EmitAnalyzer(oldAnalyzerPath, _compatibleRoslynVersion, oldDependencyPath);

            var references = this.ResolveAnalyzerReferences(newAnalyzerPath, preResolve, out var loader);

            // The analyzer itself is substituted with the copy of the old SDK. This assertion guards the
            // test setup: without it, the next assertion could pass for the wrong reason.
            var resolved = Assert.Single(references);
            Assert.Equal(oldAnalyzerPath, resolved.FullPath);

            // The dependency of the substituted analyzer must be registered with the loader, otherwise the
            // analyzer fails at run time with a FileNotFoundException.
            Assert.Contains(oldDependencyPath, loader.DependencyLocations);
        }

        /// <summary>
        /// A reference that is not substituted must not cause any additional assembly to be registered
        /// with the analyzer loader.
        /// </summary>
        [Fact]
        public void NoRedirectRegistersOnlyTheAnalyzerItself()
        {
            var newAnalyzerDirectory = this.CreateAnalyzerDirectory(_newSdkVersion);
            var oldAnalyzerDirectory = this.CreateAnalyzerDirectory(_oldSdkVersion);

            var oldDependencyPath = Path.Combine(oldAnalyzerDirectory, "TestDependency.dll");
            EmitAssembly(oldDependencyPath, "TestDependency", "public class DependencyMarker {}");

            // The analyzer of the new SDK references a Roslyn that Metalama Compiler supports, so no
            // substitution takes place.
            var newAnalyzerPath = Path.Combine(newAnalyzerDirectory, "TestAnalyzer.dll");
            this.EmitAnalyzer(newAnalyzerPath, _compatibleRoslynVersion, oldDependencyPath);

            var oldAnalyzerPath = Path.Combine(oldAnalyzerDirectory, "TestAnalyzer.dll");
            this.EmitAnalyzer(oldAnalyzerPath, _compatibleRoslynVersion, oldDependencyPath);

            var references = this.ResolveAnalyzerReferences(newAnalyzerPath, preResolve: true, out var loader);

            var resolved = Assert.Single(references);
            Assert.Equal(newAnalyzerPath, resolved.FullPath);
            Assert.Equal(new[] { newAnalyzerPath }, loader.DependencyLocations);
        }

        private IReadOnlyList<AnalyzerReference> ResolveAnalyzerReferences(
            string analyzerPath,
            bool preResolve,
            out RecordingAnalyzerAssemblyLoader loader)
        {
            Environment.SetEnvironmentVariable("METALAMA_DOTNET_ROOT", this._rootDirectory);
            DotNetInstallationLocator.ResetForTests();
            AnalyzerAssemblyRedirector.ResetForTests();

            var arguments = CSharpCommandLineParser.Default.Parse(
                new[] { "/analyzer:" + analyzerPath, "test.cs" },
                baseDirectory: this._rootDirectory,
                sdkDirectory: null);

            if (preResolve)
            {
                AnalyzerAssemblyRedirector.PreResolve(
                    arguments.AnalyzerReferences,
                    arguments.BaseDirectory,
                    arguments.ReferencePaths,
                    GetBundledRoslynVersion(),
                    new List<DiagnosticInfo>());
            }

            loader = new RecordingAnalyzerAssemblyLoader();

            return arguments.ResolveAnalyzerReferences(loader).ToArray();
        }

        /// <summary>
        /// Reads the Roslyn version bundled with Metalama Compiler the same way the compiler itself does,
        /// so that the test keeps working when that version is raised by an upstream merge.
        /// </summary>
        private static Version GetBundledRoslynVersion()
        {
            var value = typeof(CommandLineArguments).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "RoslynVersion")
                .Value;

            return Version.Parse(value!);
        }

        private string CreateAnalyzerDirectory(string sdkVersion)
        {
            // The redirector only considers analyzers under
            // <dotnet-root>/sdk/<version>/Sdks/<sdk>/{analyzers,source-generators}/.
            var directory = Path.Combine(this._rootDirectory, "sdk", sdkVersion, "Sdks", "Test.Sdk", "source-generators");
            Directory.CreateDirectory(directory);

            return directory;
        }

        private void EmitAnalyzer(string path, string roslynVersion, string dependencyPath)
        {
            var roslynStubPath = Path.Combine(this._rootDirectory, $"Microsoft.CodeAnalysis.{roslynVersion}.dll");

            if (!File.Exists(roslynStubPath))
            {
                EmitAssembly(
                    roslynStubPath,
                    "Microsoft.CodeAnalysis",
                    $"[assembly: System.Reflection.AssemblyVersion(\"{roslynVersion}\")] namespace Microsoft.CodeAnalysis {{ public class Marker {{}} }}");
            }

            // The base type and the field type make the compiler emit a reference to each of the two
            // assemblies, which is what the redirector reads.
            EmitAssembly(
                path,
                "TestAnalyzer",
                "public class AnalyzerMarker : DependencyMarker { public Microsoft.CodeAnalysis.Marker? Marker; }",
                roslynStubPath,
                dependencyPath);
        }

        private static void EmitAssembly(string path, string assemblyName, string source, params string[] references)
        {
            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { CSharpSyntaxTree.ParseText(source) },
                references
                    .Select(r => (MetadataReference)MetadataReference.CreateFromFile(r))
                    .Append(MetadataReference.CreateFromFile(typeof(object).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

            var result = compilation.Emit(path);

            Assert.True(
                result.Success,
                $"Could not emit {assemblyName}: {string.Join(Environment.NewLine, result.Diagnostics)}");
        }

        private sealed class RecordingAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
        {
            private readonly List<string> _dependencyLocations = new();

            public IReadOnlyList<string> DependencyLocations => this._dependencyLocations;

            public void AddDependencyLocation(string fullPath) => this._dependencyLocations.Add(fullPath);

            public Assembly LoadFromPath(string fullPath) => throw new NotSupportedException();
        }
    }
}
