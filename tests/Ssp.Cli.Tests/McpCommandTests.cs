using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Ssp.Cli.Tests;

public class McpCommandTests
{
    static string Cli(params string[] args)
    {
        using var output = new StringWriter();
        Program.Run(args, output);
        return output.ToString();
    }

    static Task<McpClient> Start() => McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "ssp",
        Command = "dotnet",
        Arguments = [Path.Combine(AppContext.BaseDirectory, "ssp.dll"), "mcp"],
    }));

    static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public async Task ListsFourTools()
    {
        await using var client = await Start();

        var tools = await client.ListToolsAsync();

        Assert.Equal(["explain", "render", "run", "sweep"], tools.Select(t => t.Name).Order().ToArray());
    }

    [Fact]
    public async Task RunReturnsTheCliJson()
    {
        var file = RepoPaths.Fixture("rc-lowpass.cir");
        await using var client = await Start();

        var result = await client.CallToolAsync("run", Args(("file", file)));

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(RunCommandTests.Stable(Cli("run", file, "--json")), RunCommandTests.Stable(Text(result)));
    }

    [Fact]
    public async Task ExplainReturnsTheCliReport()
    {
        var file = RepoPaths.Fixture("rc-lowpass.cir");
        await using var client = await Start();

        var result = await client.CallToolAsync("explain", Args(("file", file)));

        Assert.Equal(Cli("run", file), Text(result));
    }

    [Fact]
    public async Task SweepReturnsTheCliJson()
    {
        var file = RepoPaths.Fixture("rc-lowpass.cir");
        await using var client = await Start();

        var result = await client.CallToolAsync("sweep", Args(("file", file), ("set", "R1=4k7,10k")));

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("\"part\": \"R1\"", Text(result));
    }

    [Fact]
    public async Task ARunThatFailsGivesAnErrorResult()
    {
        await using var client = await Start();

        var result = await client.CallToolAsync("run", Args(("file", "/no/such.cir")));

        Assert.True(result.IsError);
        Assert.Contains("does not exist", Text(result));
    }
}
