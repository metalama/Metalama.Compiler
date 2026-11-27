// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Metalama.Compiler.Services;
using Microsoft.CodeAnalysis.CSharp.CommandLine.UnitTests;
using Xunit;
using Xunit.Abstractions;

namespace Metalama.Compiler.UnitTests
{
    public class LoggingTests : CommandLineTestBase
    {
        private readonly ITestOutputHelper _testOutput;

        public LoggingTests(ITestOutputHelper testOutput)
        {
            _testOutput = testOutput;
        }

        /// <summary>
        /// Tests that a compilation error in the user code is not written to the error log writer,
        /// because the error writer of the logger creates a log file even when logging is disabled (#2426).
        /// </summary>
        [Fact]
        public void CompilationErrorsAreNotLoggedAsErrors()
        {
            var dir = Temp.CreateDirectory();
            var src = dir.CreateFile("temp.cs").WriteAllText("class C { int M1() { } int M2() { } }");

            var args = new[] { "/t:library", "/preferreduilang:en-us", src.Path };

            var logger = new RecordingLogger();
            var transformers = new ISourceTransformer[] { new LoggerTransformer(logger) };
            var csc = CreateCSharpCompiler(null, dir.Path, args, transformers: transformers);

            var outWriter = new StringWriter(CultureInfo.InvariantCulture);
            var exitCode = csc.Run(outWriter);
            var output = outWriter.ToString();
            _testOutput.WriteLine(output);

            Assert.Equal(1, exitCode);
            Assert.Contains("error CS0161: 'C.M1()': not all code paths return a value", output);
            Assert.Contains("error CS0161: 'C.M2()': not all code paths return a value", output);

            // The errors are written to the information log writer, not to the error log writer.
            Assert.Empty(logger.ErrorMessages);

            var failureMessages = logger.InfoMessages.Where(m => m.Contains("The compilation failed at stage")).ToList();
            Assert.NotEmpty(failureMessages);

            // Each message contains exactly one diagnostic.
            foreach (var message in failureMessages)
            {
                Assert.Single(new[] { "C.M1()", "C.M2()" }, method => message.Contains(method));
            }

            Assert.Contains(failureMessages, m => m.Contains("C.M1()"));
            Assert.Contains(failureMessages, m => m.Contains("C.M2()"));
        }

        private sealed class LoggerTransformer : ISourceTransformerWithServices
        {
            private readonly RecordingLogger _logger;

            public LoggerTransformer(RecordingLogger logger)
            {
                _logger = logger;
            }

            public IServiceProvider? InitializeServices(InitializeServicesContext context) => new LoggerServiceProvider(_logger);

            public void Execute(TransformerContext context)
            {
            }
        }

        private sealed class LoggerServiceProvider : IServiceProvider
        {
            private readonly RecordingLogger _logger;

            public LoggerServiceProvider(RecordingLogger logger)
            {
                _logger = logger;
            }

            public object? GetService(Type serviceType) => serviceType == typeof(ILogger) ? _logger : null;
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly RecordingLogWriter _trace = new();
            private readonly RecordingLogWriter _info = new();
            private readonly RecordingLogWriter _warning = new();
            private readonly RecordingLogWriter _error = new();

            public IReadOnlyList<string> InfoMessages => _info.Messages;

            public IReadOnlyList<string> ErrorMessages => _error.Messages;

            public ILogWriter Trace => _trace;

            public ILogWriter Info => _info;

            public ILogWriter Warning => _warning;

            public ILogWriter Error => _error;
        }

        private sealed class RecordingLogWriter : ILogWriter
        {
            private readonly List<string> _messages = new();

            public IReadOnlyList<string> Messages
            {
                get
                {
                    lock (_messages)
                    {
                        return _messages.ToList();
                    }
                }
            }

            public void Log(string message)
            {
                lock (_messages)
                {
                    _messages.Add(message);
                }
            }
        }
    }
}
