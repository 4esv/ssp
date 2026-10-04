using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Ssp.Core;
using Ssp.Core.Audio;
using Ssp.Core.Analysis;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;

namespace Ssp.Web.Hosting;

/// <summary>
/// The methods that wwwroot/js/simulation-worker.js calls in the second .NET runtime, in the Web Worker.
/// Each method returns JSON, so the worker posts one string back to the page.
/// </summary>
/// <remarks>
/// NOTE: The writer does not use reflection, so the publish can trim the assembly. JSON has no NaN, so a sample
/// that is not finite is written as null.
/// </remarks>
public static partial class WorkerExports
{
    [JSExport]
    [SupportedOSPlatform("browser")]
    public static string Render(string netlist, double[] input, int sampleRate, int oversample) =>
        RenderJson(netlist, input, sampleRate, oversample);

    [JSExport]
    [SupportedOSPlatform("browser")]
    public static string Versions() => VersionsJson();

    static MonitorSession? monitor;

    [JSExport]
    [SupportedOSPlatform("browser")]
    public static string MonitorStart(string netlist, int sampleRate) => MonitorStartJson(netlist, sampleRate);

    [JSExport]
    [SupportedOSPlatform("browser")]
    public static string MonitorProcess(double[] chunk) => MonitorProcessJson(chunk);

    [JSExport]
    [SupportedOSPlatform("browser")]
    public static string MonitorStop() => MonitorStopJson();

    /// <summary>Starts the one monitor session of this runtime. A session that runs is stopped first.</summary>
    public static string MonitorStartJson(string netlist, int sampleRate)
    {
        monitor?.Dispose();
        var circuit = NetlistLoader.Load(netlist);
        Pot.Apply(circuit);
        monitor = new MonitorSession(circuit, sampleRate);
        return "true";
    }

    /// <summary>Gives the chunk to the monitor session and writes the output chunk as a JSON array. A sample that is not finite is 0.</summary>
    public static string MonitorProcessJson(double[] chunk)
    {
        var session = monitor ?? throw new InvalidOperationException("The monitor is not started.");
        var output = session.Process(chunk);
        return Write(w =>
        {
            w.WriteStartArray();
            foreach (var sample in output)
            {
                w.WriteNumberValue(double.IsFinite(sample) ? sample : 0);
            }

            w.WriteEndArray();
        });
    }

    public static string MonitorStopJson()
    {
        monitor?.Dispose();
        monitor = null;
        return "true";
    }

    /// <summary>Renders the input through the circuit and writes the output samples as a JSON array of numbers.</summary>
    public static string RenderJson(string netlist, double[] input, int sampleRate, int oversample) =>
        Write(w =>
        {
            w.WriteStartArray();
            var circuit = NetlistLoader.Load(netlist);

            // NOTE: The knob directives set the pot parts, as in Runner.Run. A knob change then changes the output.
            Pot.Apply(circuit);
            foreach (var sample in Analyses.Render(circuit, input, sampleRate, oversample))
            {
                if (double.IsFinite(sample))
                {
                    w.WriteNumberValue(sample);
                }
                else
                {
                    w.WriteNullValue();
                }
            }

            w.WriteEndArray();
        });

    /// <summary>Writes the version lines as a JSON array of strings.</summary>
    public static string VersionsJson() =>
        Write(w =>
        {
            w.WriteStartArray();
            foreach (var line in EngineInfo.VersionLines())
            {
                w.WriteStringValue(line);
            }

            w.WriteEndArray();
        });

    static string Write(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            write(w);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
