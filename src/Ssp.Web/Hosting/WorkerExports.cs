using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Ssp.Core;
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
