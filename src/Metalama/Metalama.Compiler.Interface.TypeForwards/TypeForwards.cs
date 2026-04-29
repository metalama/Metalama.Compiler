using System;
using System.Runtime.CompilerServices;
using Metalama.Compiler;
using Metalama.Compiler.Services;

[assembly: TypeForwardedTo(typeof(ISourceTransformer))]
[assembly: TypeForwardedTo(typeof(TransformerContext))]
[assembly: TypeForwardedTo(typeof(ISourceTransformerWithServices))]
[assembly: TypeForwardedTo(typeof(InitializeServicesContext))]
[assembly: TypeForwardedTo(typeof(InitializeServicesOptions))]
[assembly: TypeForwardedTo(typeof(IDisposableServiceProvider))]
[assembly: TypeForwardedTo(typeof(IExceptionReporter))]
[assembly: TypeForwardedTo(typeof(ILogger))]
[assembly: TypeForwardedTo(typeof(ILogWriter))]
[assembly: TypeForwardedTo(typeof(TransformerAttribute))]
[assembly: TypeForwardedTo(typeof(TransformerOrderAttribute))]
[assembly: TypeForwardedTo(typeof(MetalamaCompilerInfo))]
[assembly: TypeForwardedTo(typeof(SyntaxTreeTransformation))]
[assembly: TypeForwardedTo(typeof(SyntaxTreeTransformationKind))]
[assembly: TypeForwardedTo(typeof(DiagnosticFilteringRequest))]
[assembly: TypeForwardedTo(typeof(ManagedResource))]
[assembly: TypeForwardedTo(typeof(MetalamaCompilerAnnotations))]
[assembly: TypeForwardedTo(typeof(TransformerOptions))]
[assembly: TypeForwardedTo(typeof(SourceGeneratedCodeTracker))]
[assembly: TypeForwardedTo(typeof(DiagnosticFilter))]
[assembly: TypeForwardedTo(typeof(DiagnosticFilterDelegate))]
[assembly: TypeForwardedTo(typeof(DiagnosticFilterRunner))]
[assembly: TypeForwardedTo(typeof(DiagnosticFilterCollection))]

namespace Metalama.Compiler.Interface.TypeForwards
{
    public static class MetalamaCompilerInterfaces
    {
        // The C# compiler elides discarded typeof() expressions in Release. To force
        // Metalama.Compiler.Interface.dll to be loaded into the Default ALC at compiler
        // startup (so external analyzers can resolve their reference to it via
        // CompilerResolver), we use a public static field. Field assignments to public
        // observable storage cannot be elided, and the explicit static constructor
        // makes the class non-BeforeFieldInit, so the cctor runs the first time any
        // member is accessed (e.g., when CSharpCompiler.cctor calls Initialize()).
        // See https://github.com/metalama/Metalama.Compiler/issues/179.
        public static readonly Type ForcedType;

        static MetalamaCompilerInterfaces()
        {
            ForcedType = typeof(MetalamaCompilerInfo);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Initialize()
        {
        }
    }
}
