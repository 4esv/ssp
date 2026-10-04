using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Ssp.Core;

namespace Ssp.Cli;

/// <summary>
/// Serves the <c>run</c>, <c>render</c>, <c>sweep</c> and <c>explain</c> tools over MCP on standard input and output.
/// Each tool calls <see cref="Program.Run"/>, so its text is the text of the matching command.
/// </summary>
internal static class McpServerHost
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "ssp", Version = EngineInfo.VersionLines().First() },
            ToolCollection =
            [
                McpServerTool.Create(RunTool, new McpServerToolCreateOptions { Name = "run" }),
                McpServerTool.Create(RenderTool, new McpServerToolCreateOptions { Name = "render" }),
                McpServerTool.Create(SweepTool, new McpServerToolCreateOptions { Name = "sweep" }),
                McpServerTool.Create(ExplainTool, new McpServerToolCreateOptions { Name = "explain" }),
            ],
        };

        await using var server = McpServer.Create(new StdioServerTransport("ssp"), options);
        await server.RunAsync(cancellationToken);
        return 0;
    }

    [Description("Run a netlist and return the result as JSON, the same as `ssp run --json`.")]
    private static string RunTool(
        [Description("Path to the netlist.")] string file,
        [Description("Part values to set before the run, for example R1=4k7.")] string[]? set = null) =>
        Invoke(["run", file, "--json", .. SetArgs(set)]);

    [Description("Explain a netlist in plain English, the same as `ssp run`.")]
    private static string ExplainTool(
        [Description("Path to the netlist.")] string file,
        [Description("Part values to set before the run, for example R1=4k7.")] string[]? set = null) =>
        Invoke(["run", file, .. SetArgs(set)]);

    [Description("Run a netlist once for each value of a part and return the series as JSON, the same as `ssp sweep --json`.")]
    private static string SweepTool(
        [Description("Path to the netlist.")] string file,
        [Description("The part and its values, for example R1=4k7,10k,22k.")] string set) =>
        Invoke(["sweep", file, "--json", "--set", set]);

    [Description("Render a WAV file through a netlist and write the result to a WAV file, the same as `ssp render`.")]
    private static string RenderTool(
        [Description("Path to the netlist.")] string file,
        [Description("Path to the input WAV file.")] string input,
        [Description("Path to the output WAV file.")] string output,
        [Description("The number of solver steps for each sample.")] int oversample = 1,
        [Description("Path to an impulse response WAV file, for example a cabinet.")] string? ir = null)
    {
        string[] args = ["render", file, "--in", input, "--out", output, "--oversample", oversample.ToString()];
        Invoke(ir is null ? args : [.. args, "--ir", ir]);
        return $"Wrote {output}.";
    }

    private static IEnumerable<string> SetArgs(string[]? set) => (set ?? []).SelectMany(s => new[] { "--set", s });

    /// <summary>
    /// Runs a command and returns its output. An exit code of 1 from a netlist diagnostic still returns the output,
    /// as the output holds the diagnostics. A command that wrote to the error stream throws.
    /// </summary>
    private static string Invoke(string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = Program.Run(args, output, error);
        if (error.ToString().Trim().Length > 0)
        {
            throw new McpException(error.ToString().Trim());
        }

        return exitCode == 0 || output.ToString().Length > 0
            ? output.ToString()
            : throw new McpException($"ssp {args[0]} exited with code {exitCode}.");
    }
}
