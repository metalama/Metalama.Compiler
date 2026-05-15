using Microsoft.Extensions.Logging;

// Exercises the LoggerMessage source generator on .NET Framework (net48).
// Validates that Metalama.Compiler's net472 csc task host correctly loads the SDK-redirected
// analyzer through .NET Framework's strong-name verification path.

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
var logger = loggerFactory.CreateLogger("test");
LogHelpers.WidgetCount(logger, 42);

internal static partial class LogHelpers
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "WidgetCount: {Count}")]
    public static partial void WidgetCount(ILogger logger, int Count);
}
