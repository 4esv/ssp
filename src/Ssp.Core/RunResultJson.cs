using System.Numerics;
using System.Text;
using System.Text.Json;
using Ssp.Core.Analysis;
using Ssp.Core.Netlist;

namespace Ssp.Core;

/// <summary>
/// Writes a <see cref="RunResult"/> as JSON that validates against docs/schema/run-result.schema.json.
/// </summary>
/// <remarks>
/// NOTE: The writer does not use reflection, so the web app can trim the assembly.
/// JSON has no infinity or NaN. A value that is not finite is written as null, for example the magnitude in dB of node 0.
/// </remarks>
static class RunResultJson
{
    public static string Write(RunResult result)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("$schema", RunResult.SchemaId);
            w.WriteString("schemaVersion", RunResult.SchemaVersion);
            WriteOperatingPoint(w, result.OperatingPoint);
            WriteFrequencyResponse(w, result.FrequencyResponse);
            WriteImpedance(w, result.Impedance);
            WriteDiagnostics(w, result.Diagnostics);
            WriteNumbers(w, "timingsMs", result.TimingsMs);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    static void WriteOperatingPoint(Utf8JsonWriter w, OpResult? op)
    {
        if (op is null)
        {
            w.WriteNull("operatingPoint");
            return;
        }

        w.WriteStartObject("operatingPoint");
        WriteNumbers(w, "nodeVoltages", op.NodeVoltages);
        WriteNumbers(w, "sourceCurrents", op.SourceCurrents);
        WriteNumbers(w, "devicePowers", op.DevicePowers);
        w.WriteEndObject();
    }

    static void WriteFrequencyResponse(Utf8JsonWriter w, AcResult? ac)
    {
        if (ac is null)
        {
            w.WriteNull("frequencyResponse");
            return;
        }

        w.WriteStartObject("frequencyResponse");
        WriteArray(w, "frequencies", ac.Frequencies);
        WriteSeries(w, "magnitudeDb", ac.MagnitudeDb);
        WriteSeries(w, "phaseDegrees", ac.PhaseDegrees);
        w.WriteEndObject();
    }

    static void WriteImpedance(Utf8JsonWriter w, ZResult? z)
    {
        if (z is null)
        {
            w.WriteNull("impedance");
            return;
        }

        w.WriteStartObject("impedance");
        WriteArray(w, "frequencies", z.Frequencies);
        WriteComplex(w, "input", z.Input);
        WriteComplex(w, "output", z.Output);
        w.WriteEndObject();
    }

    static void WriteDiagnostics(Utf8JsonWriter w, IReadOnlyList<Diagnostic> diagnostics)
    {
        w.WriteStartArray("diagnostics");
        foreach (var d in diagnostics)
        {
            w.WriteStartObject();
            w.WriteString("severity", d.Severity == Severity.Error ? "error" : "warning");
            w.WriteString("message", d.Message);
            if (d.Line is { } line)
            {
                w.WriteNumber("line", line);
            }
            else
            {
                w.WriteNull("line");
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    static void WriteNumbers(Utf8JsonWriter w, string name, IReadOnlyDictionary<string, double> values)
    {
        w.WriteStartObject(name);
        foreach (var (key, value) in values)
        {
            w.WritePropertyName(key);
            WriteValue(w, value);
        }

        w.WriteEndObject();
    }

    static void WriteSeries(Utf8JsonWriter w, string name, IReadOnlyDictionary<string, IReadOnlyList<double>> series)
    {
        w.WriteStartObject(name);
        foreach (var (key, values) in series)
        {
            WriteArray(w, key, values);
        }

        w.WriteEndObject();
    }

    static void WriteArray(Utf8JsonWriter w, string name, IReadOnlyList<double> values)
    {
        w.WriteStartArray(name);
        foreach (var value in values)
        {
            WriteValue(w, value);
        }

        w.WriteEndArray();
    }

    static void WriteComplex(Utf8JsonWriter w, string name, IReadOnlyList<Complex> values)
    {
        w.WriteStartArray(name);
        foreach (var value in values)
        {
            w.WriteStartObject();
            w.WritePropertyName("re");
            WriteValue(w, value.Real);
            w.WritePropertyName("im");
            WriteValue(w, value.Imaginary);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    static void WriteValue(Utf8JsonWriter w, double value)
    {
        if (double.IsFinite(value))
        {
            w.WriteNumberValue(value);
        }
        else
        {
            w.WriteNullValue();
        }
    }
}
