using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;
using System.Text.Json;
using Ssp.Core;
using Ssp.Core.Analysis;
using Ssp.Core.Audio;
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
    /// Error messages go to <paramref name="error"/>, or to standard error when it is null.
    /// </summary>
    public static int Run(string[] args, TextWriter output, TextWriter? error = null)
    {
        var root = new RootCommand("Guitar pedal circuit design and simulation.");
        foreach (var option in root.Options.OfType<VersionOption>())
        {
            option.Action = new PrintVersion(output);
        }

        root.Subcommands.Add(RunCommand(output));
        root.Subcommands.Add(SweepCommand(output));
        root.Subcommands.Add(RenderCommand());

        return root.Parse(args).Invoke(new InvocationConfiguration { Output = output, Error = error ?? Console.Error });
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

    /// <summary>
    /// <c>ssp sweep file.cir --set R1=4k7,10k,22k [--json]</c>. Runs once for each value and gives one series for each run.
    /// Exits with 1 when a diagnostic in a run has severity error.
    /// </summary>
    private static Command SweepCommand(TextWriter output)
    {
        var file = new Argument<FileInfo>("file") { Description = "The netlist to run." };
        var json = new Option<bool>("--json") { Description = "Print the series as JSON." };
        var set = new Option<string>("--set")
        {
            Description = "The part and its values, for example R1=4k7,10k,22k.",
            Required = true,
        };

        var command = new Command("sweep", "Run a netlist once for each value of a part.") { file, json, set };
        command.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(file)!;
            if (!path.Exists)
            {
                parseResult.InvocationConfiguration.Error.WriteLine($"File {path.FullName} does not exist.");
                return 1;
            }

            string part;
            List<(string Text, Override Override)> values;
            try
            {
                (part, values) = ParseSweep(parseResult.GetValue(set)!);
            }
            catch (FormatException e)
            {
                parseResult.InvocationConfiguration.Error.WriteLine(e.Message);
                return 1;
            }

            var netlist = File.ReadAllText(path.FullName);
            var runs = values
                .Select(v => (v.Text, v.Override, Result: Runner.Run(netlist, new RunOptions(overrides: [v.Override]))))
                .ToList();

            var text = parseResult.GetValue(json)
                ? SweepJson(part, runs)
                : string.Join(Environment.NewLine, runs.Select(r => $"{part} = {r.Text}{Environment.NewLine}{Report.Render(r.Result)}"));
            output.Write(text);
            if (!text.EndsWith('\n'))
            {
                output.WriteLine();
            }

            return runs.Any(r => r.Result.Diagnostics.Any(d => d.Severity == Severity.Error)) ? 1 : 0;
        });

        return command;
    }

    /// <summary>
    /// <c>ssp render file.cir --in a.wav --out b.wav [--oversample N]</c>. Renders each channel of the input through the netlist
    /// and writes a 24-bit WAV file with the same sample rate and length. Exits with 1 when a file is missing or the render fails.
    /// </summary>
    private static Command RenderCommand()
    {
        var file = new Argument<FileInfo>("file") { Description = "The netlist to render through." };
        var input = new Option<FileInfo>("--in") { Description = "The input WAV file.", Required = true };
        var output = new Option<FileInfo>("--out") { Description = "The output WAV file.", Required = true };
        var oversample = new Option<int>("--oversample")
        {
            Description = "The number of solver steps for each sample.",
            DefaultValueFactory = _ => 1,
        };

        var command = new Command("render", "Render a WAV file through a netlist.") { file, input, output, oversample };
        command.SetAction(parseResult =>
        {
            var error = parseResult.InvocationConfiguration.Error;
            var path = parseResult.GetValue(file)!;
            var inPath = parseResult.GetValue(input)!;
            foreach (var f in new[] { path, inPath })
            {
                if (!f.Exists)
                {
                    error.WriteLine($"File {f.FullName} does not exist.");
                    return 1;
                }
            }

            var factor = parseResult.GetValue(oversample);
            if (factor < 1)
            {
                error.WriteLine($"Oversample {factor} must be 1 or more.");
                return 1;
            }

            var circuit = NetlistLoader.Load(File.ReadAllText(path.FullName));
            var errors = circuit.Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
            if (errors.Count > 0)
            {
                errors.ForEach(d => error.WriteLine(d.Message));
                return 1;
            }

            WavData rendered;
            try
            {
                WavData wav;
                using (var stream = inPath.OpenRead())
                {
                    wav = Wav.Read(stream);
                }

                rendered = wav with
                {
                    Channels = wav.Channels.Select(c => Analyses.Render(circuit, c, wav.SampleRate, factor)).ToArray(),
                };
            }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException or InvalidOperationException
                or ArgumentException or EndOfStreamException)
            {
                error.WriteLine(e.Message);
                return 1;
            }

            using (var stream = File.Create(parseResult.GetValue(output)!.FullName))
            {
                Wav.Write(stream, rendered, 24);
            }

            return 0;
        });

        return command;
    }

    /// <summary>Parses <c>R1=4k7,10k,22k</c> into the part and one override for each value.</summary>
    private static (string Part, List<(string Text, Override Override)> Values) ParseSweep(string text)
    {
        var parts = text.Split('=');
        if (parts.Length != 2 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0)
        {
            throw new FormatException($"Sweep '{text}' must have the form REF=VALUE,VALUE,...");
        }

        var part = parts[0].Trim();
        var values = parts[1].Split(',')
            .Select(v => v.Trim())
            .Select(v => (v, Overrides.Parse($"{part}={v}")))
            .ToList();
        return (part, values);
    }

    private static string SweepJson(string part, List<(string Text, Override Override, RunResult Result)> runs)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("part", part);
            w.WriteStartArray("series");
            foreach (var run in runs)
            {
                w.WriteStartObject();
                w.WriteString("value", run.Text);
                w.WriteNumber("number", run.Override.Value);
                w.WritePropertyName("result");
                w.WriteRawValue(run.Result.ToJson());
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
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
