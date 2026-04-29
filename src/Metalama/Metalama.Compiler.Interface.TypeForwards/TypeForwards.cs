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
        // Touch a forwarded type so the call site cannot be elided and the type-forwarder
        // metadata is exercised. Otherwise external analyzers can race CompilerResolver
        // and intermittently fail with CS8032 (issue #179).
        public static void Initialize()
        {
            _ = typeof(MetalamaCompilerInfo);
        }
    }
}
