using System.CommandLine;
using System.CommandLine.Invocation;
using Ssp.Core;
using Ssp.Core.Netlist;

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

        root.Subcommands.Add(RunCommand(output));

        return root.Parse(args).Invoke(new InvocationConfiguration { Output = output });
    }

    /// <summary>
    /// <c>ssp run file.cir [--json] [--set R1=4k7]</c>. Exits with 1 when a diagnostic has severity error.
    /// </summary>
    private static Command RunCommand(TextWriter output)
    {
        var file = new Argument<FileInfo>("file") { Description = "The netlist to run." };
        var json = new Option<bool>("--json") { Description = "Print the result as JSON." };
        var set = new Option<string[]>("--set")
        {
            Description = "Set a part value before the run, for example R1=4k7. Use it again for more parts.",
        };

        var command = new Command("run", "Run a netlist and print a report.") { file, json, set };
        command.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(file)!;
            if (!path.Exists)
            {
                parseResult.InvocationConfiguration.Error.WriteLine($"File {path.FullName} does not exist.");
                return 1;
            }

            IReadOnlyList<Override> overrides;
            try
            {
                overrides = (parseResult.GetValue(set) ?? []).Select(Overrides.Parse).ToList();
            }
            catch (FormatException e)
            {
                parseResult.InvocationConfiguration.Error.WriteLine(e.Message);
                return 1;
            }

            var result = Runner.Run(File.ReadAllText(path.FullName), new RunOptions(overrides: overrides));
            var text = parseResult.GetValue(json) ? result.ToJson() : Report.Render(result);
            output.Write(text);
            if (!text.EndsWith('\n'))
            {
                output.WriteLine();
            }

            return result.Diagnostics.Any(d => d.Severity == Severity.Error) ? 1 : 0;
        });

        return command;
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
