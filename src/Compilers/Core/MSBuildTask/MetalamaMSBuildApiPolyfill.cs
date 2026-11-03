// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// <Metalama>
// This whole file is a Metalama addition.
//
// Upstream compiles these tasks against the MSBuild that ships in the same .NET SDK, and uses the multithreaded task
// API (IMultiThreadableTask, TaskEnvironment, AbsolutePath, ToolTask.TaskEnvironment) that appeared in MSBuild 18.4
// and 18.6. Metalama ships the tasks in a NuGet package that must load on every supported .NET SDK feature band,
// including 10.0.1xx (MSBuild 18.0), which has none of these types. We therefore compile the tasks against the
// MSBuild of the lowest supported feature band (see MetalamaMSBuildTaskApiVersion in Directory.Build.targets) and
// supply the subset of the API that the upstream code uses here.
//
// The types are declared in Microsoft.CodeAnalysis.BuildTasks, not in Microsoft.Build.Framework. Types of an enclosing
// namespace take precedence over types imported with a using directive, so the upstream sources bind to these types
// without modification, even in the unit tests, which reference a newer MSBuild that has its own types.
//
// Because MSBuild does not recognize this MSBuildMultiThreadableTaskAttribute, it never runs these tasks in its
// multithreaded mode. It sets the process current directory and environment before executing them, which is
// what TaskEnvironment.Fallback reads. With -mt, MSBuild runs them out of process in a task host instead.
//
// See https://github.com/metalama/Metalama.Compiler/issues/228.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.CodeAnalysis.BuildTasks
{
    /// <summary>
    /// Stands in for <c>Microsoft.Build.Framework.MSBuildMultiThreadableTaskAttribute</c>. MSBuild does not recognize
    /// this attribute, so it has no effect.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    internal sealed class MSBuildMultiThreadableTaskAttribute : Attribute
    {
    }

    /// <summary>
    /// Stands in for <c>Microsoft.Build.Framework.IMultiThreadableTask</c>. MSBuild does not recognize this interface,
    /// so it never assigns <see cref="TaskEnvironment"/>, which keeps its initial value.
    /// </summary>
    public interface IMultiThreadableTask
    {
        TaskEnvironment TaskEnvironment { get; set; }
    }

    /// <summary>
    /// Stands in for <c>Microsoft.Build.Framework.AbsolutePath</c>.
    /// </summary>
    public readonly struct AbsolutePath : IEquatable<AbsolutePath>
    {
        private static readonly StringComparer s_pathComparer =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        public AbsolutePath(string path)
        {
            ValidatePath(path);

            if (!Path.IsPathRooted(path))
            {
                throw new ArgumentException($"The path '{path}' is not an absolute path.", nameof(path));
            }

            Value = path;
            OriginalValue = path;
        }

        public AbsolutePath(string path, AbsolutePath basePath)
        {
            ValidatePath(path);

            Value = Path.IsPathRooted(path) ? path : Path.Combine(basePath.Value, path);
            OriginalValue = path;
        }

        public string Value { get; }

        public string OriginalValue { get; }

        private static void ValidatePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("The path must not be null or empty.", nameof(path));
            }
        }

        public static implicit operator string(AbsolutePath path) => path.Value;

        public static bool operator ==(AbsolutePath left, AbsolutePath right) => left.Equals(right);

        public static bool operator !=(AbsolutePath left, AbsolutePath right) => !left.Equals(right);

        public bool Equals(AbsolutePath other) => s_pathComparer.Equals(Value ?? "", other.Value ?? "");

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is AbsolutePath other && Equals(other);

        public override int GetHashCode() => Value is null ? 0 : s_pathComparer.GetHashCode(Value);

        public override string ToString() => Value;
    }

    /// <summary>
    /// Stands in for <c>Microsoft.Build.Framework.TaskEnvironment</c>. <see cref="Fallback"/> reads the process state,
    /// which is what MSBuild sets up for a task that is not multithreadable.
    /// <see cref="CreateWithProjectDirectoryAndEnvironment"/> creates an isolated environment, for tests.
    /// </summary>
    public sealed class TaskEnvironment
    {
        private readonly string? _projectDirectory;
        private readonly Dictionary<string, string>? _environmentVariables;

        private TaskEnvironment(string? projectDirectory, Dictionary<string, string>? environmentVariables)
        {
            _projectDirectory = projectDirectory;
            _environmentVariables = environmentVariables;
        }

        private static StringComparer EnvironmentVariableComparer =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        /// <summary>
        /// Gets the environment of the current process.
        /// </summary>
        public static TaskEnvironment Fallback { get; } = new TaskEnvironment(null, null);

        public static TaskEnvironment CreateWithProjectDirectoryAndEnvironment(
            string projectDirectory,
            IDictionary<string, string>? environmentVariables = null)
        {
            var copy = new Dictionary<string, string>(EnvironmentVariableComparer);

            if (environmentVariables is null)
            {
#pragma warning disable RS0030 // Do not use banned APIs: this captures the process environment on purpose.
                foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
#pragma warning restore RS0030
                {
                    copy[(string)entry.Key] = (string?)entry.Value ?? "";
                }
            }
            else
            {
                foreach (var pair in environmentVariables)
                {
                    copy[pair.Key] = pair.Value;
                }
            }

            return new TaskEnvironment(new AbsolutePath(projectDirectory).Value, copy);
        }

#pragma warning disable RS0030 // Do not use banned APIs: the fallback environment is the process environment.
        public AbsolutePath ProjectDirectory => new AbsolutePath(_projectDirectory ?? Directory.GetCurrentDirectory());

        public string? GetEnvironmentVariable(string name)
            => _environmentVariables is null
                ? Environment.GetEnvironmentVariable(name)
                : _environmentVariables.TryGetValue(name, out var value) ? value : null;

        public IReadOnlyDictionary<string, string> GetEnvironmentVariables()
        {
            if (_environmentVariables is not null)
            {
                return new Dictionary<string, string>(_environmentVariables, EnvironmentVariableComparer);
            }

            var result = new Dictionary<string, string>(EnvironmentVariableComparer);

            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                result[(string)entry.Key] = (string?)entry.Value ?? "";
            }

            return result;
        }
#pragma warning restore RS0030

        public AbsolutePath GetAbsolutePath(string path) => new AbsolutePath(path, ProjectDirectory);
    }
}
// </Metalama>
