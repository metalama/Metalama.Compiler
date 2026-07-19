using XenoAtom.Logging;
using XenoAtom.Logging.Writers;

namespace Issue193;

public partial class Program
{
    public static Logger Logger { get; internal set; } = null!;

    // The partial property is implemented by XenoAtom.Logging's LogFormatterGenerator.
    // If the generator fails to load, this stays unimplemented and the build fails
    // with CS9248 -- preceded by the CS8785 that is the actual symptom of issue #193.
    [LogFormatter("[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level}] [{LoggerName}] {Text}")]
    public static partial LogFormatter MyLogFormatter { get; }

    private static void Main()
    {
        LogManager.Initialize(new()
        {
            RootLogger =
            {
                MinimumLevel = LogLevel.Info,
                Writers =
                {
                    new StreamLogWriter(Console.OpenStandardOutput())
                    {
                        Formatter = MyLogFormatter with
                        {
                            LevelFormat = LogLevelFormat.Long
                        }
                    }
                }
            }
        });

        Logger = LogManager.GetLogger("Program");
        Logger.Info("Hello, World!");
    }
}
