using System.CommandLine;
using System.CommandLine.Invocation;
using Ssp.Core;

namespace Ssp.Cli;

/// <summary>
/// The ssp command line entry point.
/// </summary>
public static class Program
{
    public static int Main(string[] args) => Run(args, Console.Out);

    /// <summary>
    /// Runs the command line with the given arguments and writes all output to <paramref name="output"/>.
    /// </summary>
    public static int Run(string[] args, TextWriter output)
    {
        var root = new RootCommand("Guitar pedal circuit design and simulation.");
        foreach (var option in root.Options.OfType<VersionOption>())
        {
            option.Action = new PrintVersion(output);
        }

        return root.Parse(args).Invoke(new InvocationConfiguration { Output = output });
    }

    private sealed class PrintVersion(TextWriter output) : SynchronousCommandLineAction
    {
        public override int Invoke(ParseResult parseResult)
        {
            foreach (var line in EngineInfo.VersionLines())
            {
                output.WriteLine(line);
            }

            return 0;
        }
    }
}
