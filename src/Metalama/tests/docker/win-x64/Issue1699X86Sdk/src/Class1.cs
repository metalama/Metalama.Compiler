namespace Issue1699;

// Trivial type; the point of this scenario is that the project compiles at all
// under an x86 SDK whose $(MSBuildToolsPath) contains parentheses.
public class Class1
{
    public int Answer => 42;
}
