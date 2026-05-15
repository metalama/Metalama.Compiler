using Microsoft.Extensions.Logging;

// Exercises the LoggerMessage source generator (Microsoft.Extensions.Logging.Generators).
// If the generator fails to load (issue #180 regression), the partial method below has no
// implementation and the program won't compile. Successful build + expected output confirms
// the generator ran and emitted code that the runtime executes correctly.

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
var logger = loggerFactory.CreateLogger("test");
LogHelpers.WidgetCount(logger, 42);

internal static partial class LogHelpers
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "WidgetCount: {Count}")]
    public static partial void WidgetCount(ILogger logger, int Count);
}
