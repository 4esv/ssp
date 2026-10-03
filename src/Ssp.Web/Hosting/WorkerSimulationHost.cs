using System.Text.Json;
using Microsoft.JSInterop;
using Ssp.Core;

namespace Ssp.Web.Hosting;

/// <summary>
/// Runs <c>Ssp.Core</c> in a second .NET runtime in a Web Worker, so a long render does not block the page.
/// The page posts the netlist to the worker. The worker returns JSON. See <see cref="WorkerExports"/>.
/// </summary>
/// <remarks>
/// NOTE: <see cref="Run"/> and <see cref="Sweep"/> stay on the calling thread. They take milliseconds, and the web app
/// has no reader for the run result JSON yet.
/// </remarks>
public sealed class WorkerSimulationHost(IJSRuntime js) : ISimulationHost
{
    /// <summary>The page module that starts the worker and posts the requests.</summary>
    public const string ClientModule = "./js/simulation-worker-client.js";

    readonly InProcessSimulationHost inProcess = new();
    IJSObjectReference? module;

    public Task<RunResult> Run(string netlist, RunOptions options) => inProcess.Run(netlist, options);

    public async Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample) =>
        ReadSamples(await Call("render", netlist, input, sampleRate, oversample));

    public Task<SweepResult> Sweep(string netlist, string reference, IReadOnlyList<double> values, RunOptions options) =>
        inProcess.Sweep(netlist, reference, values, options);

    public async Task<IReadOnlyList<string>> Versions()
    {
        using var document = JsonDocument.Parse(await Call("versions"));
        return [.. document.RootElement.EnumerateArray().Select(line => line.GetString()!)];
    }

    /// <summary>Reads the samples that <see cref="WorkerExports.Render"/> writes. A null sample is NaN.</summary>
    public static double[] ReadSamples(string json)
    {
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateArray()
            .Select(sample => sample.ValueKind == JsonValueKind.Null ? double.NaN : sample.GetDouble())];
    }

    async Task<string> Call(string method, params object[] args)
    {
        module ??= await js.InvokeAsync<IJSObjectReference>("import", ClientModule);
        return await module.InvokeAsync<string>(method, args);
    }
}
