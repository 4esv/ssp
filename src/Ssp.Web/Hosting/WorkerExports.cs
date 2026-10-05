using System.Collections.Concurrent;
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
        RenderJson(netlist, input, sampleRate, oversample, done => Progress(done / (double)sampleRate));

    [JSExport]
    [SupportedOSPlatform("browser")]
    public static string Convolve(double[] samples, double[] ir) => SamplesJson(Convolution.Convolve(samples, ir));

    [JSExport]
    [SupportedOSPlatform("browser")]
    public static string Live(string netlist) => LiveJson(netlist);

    /// <summary>
    /// Runs <see cref="InProcessSimulationHost.LiveResult"/> and writes the diagnostics and the operating point.
    /// <see cref="WorkerSimulationHost.ReadLive"/> reads it back. A value that is not finite is null.
    /// </summary>
    public static string LiveJson(string netlist)
    {
        var result = InProcessSimulationHost.LiveResult(netlist);
        return Write(w =>
        {
            w.WriteStartObject();
            w.WriteStartArray("diagnostics");
            foreach (var d in result.Diagnostics)
            {
                w.WriteStartObject();
                w.WriteString("severity", d.Severity.ToString());
                w.WriteString("message", d.Message);
                if (d.Line is int line)
                {
                    w.WriteNumber("line", line);
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();
            if (result.OperatingPoint is { } op)
            {
                w.WriteStartObject("operatingPoint");
                Values("nodeVoltages", op.NodeVoltages);
                Values("sourceCurrents", op.SourceCurrents);
                Values("devicePowers", op.DevicePowers);
                w.WriteEndObject();
            }

            w.WriteEndObject();

            void Values(string name, IReadOnlyDictionary<string, double> values)
            {
                w.WriteStartObject(name);
                foreach (var (key, value) in values)
                {
                    if (double.IsFinite(value))
                    {
                        w.WriteNumber(key, value);
                    }
                    else
                    {
                        w.WriteNull(key);
                    }
                }

                w.WriteEndObject();
            }
        });
    }

    /// <summary>Tells the worker script the seconds of audio done. The script posts it to the page.</summary>
    [JSImport("progress", "worker")]
    [SupportedOSPlatform("browser")]
    static partial void Progress(double seconds);

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

    // The netlist texts whose last render used variable steps, for this runtime.
    static readonly ConcurrentDictionary<string, bool> variable = new(StringComparer.Ordinal);

    /// <summary>True if a render of this netlist text in this runtime used variable steps. The next render then starts on them.</summary>
    public static bool StartsOnVariableSteps(string netlist) => variable.ContainsKey(netlist);

    /// <summary>
    /// Renders the input through the circuit and writes the output samples as a JSON array of numbers.
    /// A netlist text that needed variable steps before starts on them, and does not try fixed steps again.
    /// </summary>
    public static string RenderJson(string netlist, double[] input, int sampleRate, int oversample, Action<int>? progress = null) =>
        Write(w =>
        {
            w.WriteStartArray();
            var circuit = NetlistLoader.Load(netlist);

            // NOTE: The knob directives set the pot parts, as in Runner.Run. A knob change then changes the output.
            Pot.Apply(circuit);
            var statistics = new RenderStatistics();
            var output = Analyses.Render(circuit, input, sampleRate, oversample, progress, statistics, StartsOnVariableSteps(netlist));
            if (statistics.VariableSteps)
            {
                variable[netlist] = true;
            }

            foreach (var sample in output)
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

    /// <summary>Writes the samples as a JSON array of numbers. A sample that is not finite is null.</summary>
    public static string SamplesJson(double[] samples) =>
        Write(w =>
        {
            w.WriteStartArray();
            foreach (var sample in samples)
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
