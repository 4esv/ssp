using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;
using Ssp.Core.Analysis;
using Ssp.Core.Netlist;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

/// <summary>
/// Measures the wall time of <see cref="Analyses.Render"/> in Chromium.
/// </summary>
/// <remarks>
/// NOTE: Ssp.Web has no render entry point yet, and its publish trims Render away. The test publishes a small
/// WebAssembly page that calls Ssp.Core through one [JSExport] method. The runtime is the same Mono WebAssembly
/// runtime that Ssp.Web uses.
/// </remarks>
[Collection(PlaywrightCollection.Name)]
public class EngineSpeedTests(ITestOutputHelper output)
{
    private const int Fs = 44_100;
    private const double Amplitude = 0.3;
    private const double Frequency = 440.0;
    private const string Origin = "http://localhost:5199";

    // The browser and native .NET use different math libraries. 1 uV is far below the 18 mV that a model
    // change moves the clamp (see RenderTests).
    private const double MaxSampleDifference = 1e-6;

    private const string ProjectFile = """
        <Project Sdk="Microsoft.NET.Sdk.WebAssembly">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <OutputType>Exe</OutputType>
            <Nullable>enable</Nullable>
            <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
            <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
            <WasmFingerprintAssets>false</WasmFingerprintAssets>
            <PublishTrimmed>false</PublishTrimmed>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="CORE_PROJECT" />
          </ItemGroup>
        </Project>
        """;

    private const string ProgramFile = """
        using System.Runtime.InteropServices.JavaScript;
        using Ssp.Core.Analysis;
        using Ssp.Core.Netlist;

        public static partial class Bench
        {
            public static void Main()
            {
            }

            [JSExport]
            public static double[] Render(string netlist, double[] input, int fs, int oversample) =>
                Analyses.Render(NetlistLoader.Load(netlist), input, fs, oversample);
        }
        """;

    private const string IndexFile = """
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><title>engine speed</title></head>
        <body><script type="module" src="main.js"></script></body></html>
        """;

    // The page times the call, so the number includes the JS to .NET marshalling of the samples.
    private const string MainFile = """
        import { dotnet } from './_framework/dotnet.js';
        const runtime = await dotnet.create();
        const bench = (await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName)).Bench;
        globalThis.sspRender = (netlist, input, fs, oversample) => {
          const start = performance.now();
          const samples = bench.Render(netlist, input, fs, oversample);
          return { ms: performance.now() - start, samples: Array.from(samples) };
        };
        globalThis.sspReady = true;
        """;

    [PlaywrightFact]
    public async Task RenderOneSecondOfClipperInBrowser()
    {
        var site = PublishBenchPage();
        var netlist = File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", "clipper-bjt-si.cir"));
        var oneSecond = Sine(Fs);
        var native = Analyses.Render(NetlistLoader.Load(netlist), oneSecond, Fs, 1);

        var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
        var install = withDeps ? new[] { "install", "--with-deps", "chromium" } : new[] { "install", "chromium" };
        Assert.Equal(0, Microsoft.Playwright.Program.Main(install));

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await page.RouteAsync(Origin + "/**", route => Serve(route, site));
        await page.GotoAsync(Origin + "/");
        await page.WaitForFunctionAsync("() => globalThis.sspReady === true", null, new() { Timeout = 120_000 });

        var cold = await RenderInPage(page, netlist, oneSecond);
        var warm = await RenderInPage(page, netlist, oneSecond);
        var tenth = await RenderInPage(page, netlist, Sine(Fs / 10));

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"1 s, first call: {cold.Ms:F0} ms"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"1 s, second call: {warm.Ms:F0} ms"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"0.1 s: {tenth.Ms:F0} ms"));

        Assert.Equal(native.Length, warm.Samples.Length);
        Assert.Equal(Fs / 10, tenth.Samples.Length);
        Assert.All(warm.Samples, sample => Assert.True(double.IsFinite(sample)));
        var difference = native.Zip(warm.Samples, (a, b) => Math.Abs(a - b)).Max();
        Assert.True(difference < MaxSampleDifference, $"The browser output differs from native by {difference} V.");
        Assert.Equal(warm.Samples, cold.Samples);
    }

    private static double[] Sine(int samples)
    {
        var input = new double[samples];
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = Amplitude * Math.Sin(2.0 * Math.PI * Frequency * i / Fs);
        }

        return input;
    }

    private static async Task<(double Ms, double[] Samples)> RenderInPage(IPage page, string netlist, double[] input)
    {
        var result = await page.EvaluateAsync<JsonElement>(
            "([netlist, input, fs]) => globalThis.sspRender(netlist, input, fs, 1)",
            new object[] { netlist, input, Fs });
        var samples = result.GetProperty("samples").EnumerateArray().Select(s => s.GetDouble()).ToArray();
        return (result.GetProperty("ms").GetDouble(), samples);
    }

    /// <summary>Writes the bench page project to a temporary folder, publishes it, and returns the site folder.</summary>
    private string PublishBenchPage()
    {
        var dir = Directory.CreateTempSubdirectory("ssp-engine-speed-").FullName;
        var core = Path.Combine(RepoPaths.Root, "src", "Ssp.Core", "Ssp.Core.csproj");
        File.WriteAllText(Path.Combine(dir, "Bench.csproj"), ProjectFile.Replace("CORE_PROJECT", core, StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(dir, "Program.cs"), ProgramFile);
        Directory.CreateDirectory(Path.Combine(dir, "wwwroot"));
        File.WriteAllText(Path.Combine(dir, "wwwroot", "index.html"), IndexFile);
        File.WriteAllText(Path.Combine(dir, "wwwroot", "main.js"), MainFile);

        // NOTE: The working directory is the repository, so global.json picks the SDK.
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = RepoPaths.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "publish", Path.Combine(dir, "Bench.csproj"), "-c", "Release", "-o", Path.Combine(dir, "out"), "-nodeReuse:false" })
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, "The bench page did not publish:\n" + stdout + stderr.Result);
        output.WriteLine("Published the bench page to " + dir);
        return Path.Combine(dir, "out", "wwwroot");
    }

    private static Task Serve(IRoute route, string site)
    {
        var path = new Uri(route.Request.Url).AbsolutePath.TrimStart('/');
        var file = Path.GetFullPath(Path.Combine(site, path.Length == 0 ? "index.html" : path));
        if (!file.StartsWith(site, StringComparison.Ordinal) || !File.Exists(file))
        {
            return route.FulfillAsync(new() { Status = 404 });
        }

        var type = Path.GetExtension(file) switch
        {
            ".html" => "text/html",
            ".js" => "text/javascript",
            ".json" => "application/json",
            ".wasm" => "application/wasm",
            _ => "application/octet-stream",
        };
        return route.FulfillAsync(new() { Status = 200, ContentType = type, BodyBytes = File.ReadAllBytes(file) });
    }
}
