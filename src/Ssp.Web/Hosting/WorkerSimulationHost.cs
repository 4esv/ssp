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
public sealed class WorkerSimulationHost(IJSRuntime js) : ISimulationHost, IMonitorHost
{
    /// <summary>The page module that starts the worker and posts the requests.</summary>
    public const string ClientModule = "./js/simulation-worker-client.js";

    readonly InProcessSimulationHost inProcess = new();
    IJSObjectReference? module;
    bool cancelled;

    /// <summary>Receives the progress calls of the worker script.</summary>
    sealed class ProgressSink(Action<double> report)
    {
        [JSInvokable]
        public void Report(double seconds) => report(seconds);
    }

    // NOTE: A cancel ends the worker, so the pending call fails in JS. The failure is a cancel, not an error.
    async Task<string> Cancellable(Func<Task<string>> call)
    {
        cancelled = false;
        try
        {
            return await call();
        }
        catch (JSException) when (cancelled)
        {
            throw new OperationCanceledException();
        }
    }

    public Task<RunResult> Run(string netlist, RunOptions options) => inProcess.Run(netlist, options);

    public Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample) =>
        Render(netlist, input, sampleRate, oversample, null);

    public async Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample, Action<double>? onProgress)
    {
        using var sink = onProgress is null ? null : DotNetObjectReference.Create(new ProgressSink(onProgress));
        return ReadSamples(await Cancellable(() => Call("render", netlist, input, sampleRate, oversample, sink!)));
    }

    public async Task<double[]> Convolve(double[] samples, double[] ir) =>
        ReadSamples(await Cancellable(() => Call("convolve", samples, ir)));

    public void CancelRender()
    {
        cancelled = true;
        _ = module?.InvokeVoidAsync("cancel");
    }

    public async Task MonitorStart(string netlist, int sampleRate) => await Call("monitorStart", netlist, sampleRate);

    public async Task<double[]> MonitorProcess(double[] chunk) => ReadSamples(await Call("monitorProcess", chunk));

    public async Task MonitorStop() => await Call("monitorStop");

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
