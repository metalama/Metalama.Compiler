// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// <Metalama>
// This whole file is a Metalama addition. It tests MetalamaMSBuildApiPolyfill.cs.
// See https://github.com/metalama/Metalama.Compiler/issues/228.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Xunit;

namespace Microsoft.CodeAnalysis.BuildTasks.UnitTests
{
    public sealed class MetalamaMSBuildApiPolyfillTests
    {
        /// <summary>
        /// The task assembly must load on MSBuild 18.0 (.NET SDK 10.0.1xx), which has none of these types. A reference
        /// to one of them means that the task assembly binds to the MSBuild type instead of the polyfill.
        /// </summary>
        [Fact]
        public void TaskAssemblyDoesNotReferenceNewerMSBuildTypes()
        {
            var newerTypes = new[] { "IMultiThreadableTask", "TaskEnvironment", "AbsolutePath", "MSBuildMultiThreadableTaskAttribute" };

            using var stream = File.OpenRead(typeof(ManagedCompiler).Assembly.Location);
            using var peReader = new PEReader(stream);
            var metadataReader = peReader.GetMetadataReader();

            var offending = metadataReader.TypeReferences
                .Select(handle => metadataReader.GetTypeReference(handle))
                .Where(typeReference => metadataReader.GetString(typeReference.Namespace) == "Microsoft.Build.Framework")
                .Select(typeReference => metadataReader.GetString(typeReference.Name))
                .Where(name => newerTypes.Contains(name))
                .ToList();

            Assert.Empty(offending);
        }

        [Fact]
        public void TasksBindToThePolyfill()
        {
            Assert.Same(typeof(TaskEnvironment).Assembly, typeof(ManagedCompiler).Assembly);
            Assert.Same(typeof(IMultiThreadableTask).Assembly, typeof(ManagedCompiler).Assembly);
            Assert.True(typeof(IMultiThreadableTask).IsAssignableFrom(typeof(CopyRefAssembly)));
        }

        [Fact]
        public void AbsolutePathCombinesRelativePathWithBase()
        {
            var basePath = new AbsolutePath(TempRoot());
            var path = new AbsolutePath(Path.Combine("sub", "file.cs"), basePath);

            Assert.Equal(Path.Combine(TempRoot(), "sub", "file.cs"), path.Value);
            Assert.Equal(Path.Combine("sub", "file.cs"), path.OriginalValue);
            Assert.Equal(path.Value, (string)path);
        }

        [Fact]
        public void AbsolutePathKeepsRootedPath()
        {
            var rooted = Path.Combine(TempRoot(), "file.cs");
            var path = new AbsolutePath(rooted, new AbsolutePath(Path.Combine(TempRoot(), "other")));

            Assert.Equal(rooted, path.Value);
        }

        [Fact]
        public void AbsolutePathAcceptsCharactersInvalidInPath()
        {
            var path = new AbsolutePath("bad|ref.dll", new AbsolutePath(TempRoot()));

            Assert.Equal(TempRoot() + Path.DirectorySeparatorChar + "bad|ref.dll", path.Value);
        }

        [Fact]
        public void AbsolutePathRejectsInvalidPaths()
        {
            Assert.Throws<ArgumentException>(() => new AbsolutePath(""));
            Assert.Throws<ArgumentException>(() => new AbsolutePath("relative"));
            Assert.Throws<ArgumentException>(() => new AbsolutePath("", new AbsolutePath(TempRoot())));
        }

        [Fact]
        public void AbsolutePathEquality()
        {
            var a = new AbsolutePath(Path.Combine(TempRoot(), "File.cs"));
            var b = new AbsolutePath(Path.Combine(TempRoot(), "File.cs"));
            var lower = new AbsolutePath(Path.Combine(TempRoot(), "file.cs"));

            Assert.True(a == b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());

            var caseInsensitive = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            Assert.Equal(caseInsensitive, a == lower);
        }

        [Fact]
        public void IsolatedEnvironmentUsesGivenDirectoryAndVariables()
        {
            var projectDirectory = Path.Combine(TempRoot(), "project");
            var environment = TaskEnvironment.CreateWithProjectDirectoryAndEnvironment(
                projectDirectory,
                new Dictionary<string, string> { ["METALAMA_POLYFILL_TEST"] = "value" });

            Assert.Equal(projectDirectory, environment.ProjectDirectory.Value);
            Assert.Equal(Path.Combine(projectDirectory, "file.cs"), environment.GetAbsolutePath("file.cs").Value);
            Assert.Equal("value", environment.GetEnvironmentVariable("METALAMA_POLYFILL_TEST"));
            Assert.Null(environment.GetEnvironmentVariable("PATH"));
            Assert.Equal(new[] { "METALAMA_POLYFILL_TEST" }, environment.GetEnvironmentVariables().Keys);

            var caseInsensitive = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            Assert.Equal(caseInsensitive ? "value" : null, environment.GetEnvironmentVariable("metalama_polyfill_test"));
        }

        [Fact]
        public void IsolatedEnvironmentIsNotAffectedByLaterChanges()
        {
            var variables = new Dictionary<string, string> { ["A"] = "1" };
            var environment = TaskEnvironment.CreateWithProjectDirectoryAndEnvironment(TempRoot(), variables);

            variables["A"] = "2";
            variables["B"] = "3";

            Assert.Equal("1", environment.GetEnvironmentVariable("A"));
            Assert.Null(environment.GetEnvironmentVariable("B"));
        }

        [Fact]
        public void IsolatedEnvironmentWithoutVariablesCapturesProcessEnvironment()
        {
            var environment = TaskEnvironment.CreateWithProjectDirectoryAndEnvironment(TempRoot());

            Assert.Equal(Environment.GetEnvironmentVariable("PATH"), environment.GetEnvironmentVariable("PATH"));
        }

        [Fact]
        public void FallbackReadsProcessState()
        {
            Assert.Equal(Directory.GetCurrentDirectory(), TaskEnvironment.Fallback.ProjectDirectory.Value);
            Assert.Equal(Environment.GetEnvironmentVariable("PATH"), TaskEnvironment.Fallback.GetEnvironmentVariable("PATH"));
            Assert.Equal(
                Environment.GetEnvironmentVariables().Count,
                TaskEnvironment.Fallback.GetEnvironmentVariables().Count);
        }

        [Fact]
        public void ManagedToolTaskDefaultsToFallback()
        {
            Assert.Same(TaskEnvironment.Fallback, new Csc().TaskEnvironment);
        }

        private static string TempRoot() => Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
// </Metalama>
