using System.Diagnostics;
using SpiceSharp;
using SpiceSharp.Behaviors;
using SpiceSharp.Algebra;
using SpiceSharp.Components;
using SpiceSharp.Simulations;
using SpiceSharp.Simulations.IntegrationMethods;
using Ssp.Core.Analysis;
using Ssp.Core.Netlist;
using Xunit.Abstractions;

namespace Ssp.Core.Tests.Spikes;

/// <summary>
/// Spike for #62: the real-time factor of a time-step loop with one Newton iteration per sample.
/// The engine setting TransientMaxIterations=1 only fails convergence, so this loop takes its own step:
/// it loads, factors and solves once, then accepts the solution without a convergence check.
/// </summary>
public class OneIterationTransient(ITestOutputHelper output)
{
    private const int Fs = 44_100;
    private const double Amplitude = 0.3;
    private const double Frequency = 440.0;
    private const int Runs = 5;

    private sealed class OneIteration : Transient
    {
        public OneIteration(string name, TimeParameters parameters)
            : base(name, parameters)
        {
        }

        public int Steps { get; private set; }

        // The engine loop (Transient.Execute in SpiceSharp 3.2.3) with its Newton loop cut to one pass: temperature,
        // the operating point, then for each sample one load, factor and solve that is accepted without a convergence check.
        protected override IEnumerable<int> Execute(int mask)
        {
            foreach (var behavior in EntityBehaviors.GetBehaviorList<ITemperatureBehavior>())
            {
                behavior.Temperature();
            }

            var method = GetState<IIntegrationMethod>();
            var state = GetState<IBiasingSimulationState>();
            var time = GetState<ITimeSimulationState>();
            var useDc = time.GetType().GetProperty(nameof(ITimeSimulationState.UseDc))!;
            useDc.SetValue(time, true);
            Op(BiasingParameters.DcMaxIterations);
            InitializeStates();
            useDc.SetValue(time, false);
            Iteration.Mode = IterationModes.Float;

            var reorder = true;
            while (true)
            {
                Accept();
                if ((mask & ExportTransient) != 0)
                {
                    yield return ExportTransient;
                }

                if (method.Time >= TimeParameters.StopTime)
                {
                    break;
                }

                method.Prepare();
                Probe();
                Load();
                if (reorder || !state.Solver.Factor())
                {
                    var rank = state.Solver.OrderAndFactor();
                    if (rank < state.Solver.Size)
                    {
                        throw new SingularException(rank + 1);
                    }

                    reorder = false;
                }

                StoreSolution();
                state.Solver.ForwardSubstitute(state.Solution);
                state.Solver.BackwardSubstitute(state.Solution);
                state.Solution[0] = 0.0;
                state.OldSolution[0] = 0.0;

                // NOTE: Load computes the charge and current states from the solution it loads. Without this load,
                // Accept stores the states of the previous sample and the trapezoidal recurrence diverges to NaN.
                // It loads only: there is no second factor or solve.
                Load();
                _ = method.Evaluate(double.PositiveInfinity);
                Steps++;
            }
        }
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

    private static LoadedCircuit Load() => NetlistLoader.Load(Fixtures.Read("clipper-bjt-si.cir"));

    // The same drive as Analyses.Render: a piecewise-linear source on the input node, fixed steps of 1 / fs.
    private static (double[] Output, int Steps) RenderOneIteration(LoadedCircuit circuit, double[] input)
    {
        var inNode = circuit.Directives.Input ?? throw new InvalidOperationException("No ssp:input node.");
        var outNode = circuit.Directives.Output ?? "out";

        var points = new double[2 * Math.Max(input.Length, 2)];
        for (var i = 0; i < points.Length / 2; i++)
        {
            points[2 * i] = (double)i / Fs;
            points[(2 * i) + 1] = input[Math.Min(i, input.Length - 1)];
        }

        var pwl = new Pwl();
        pwl.SetPoints(points);
        var source = circuit.Circuit.OfType<VoltageSource>()
            .First(v => v.Nodes[0] == inNode && v.Nodes[1] == "0");
        var saved = source.Parameters.Waveform;
        var step = 1.0 / Fs;
        var result = new double[input.Length];
        var written = 0;
        try
        {
            source.Parameters.Waveform = pwl;
            var tran = new OneIteration("one", new FixedTrapezoidal { Step = step, StopTime = (input.Length - 1) * step });
            var voltage = new RealVoltageExport(tran, outNode);
            foreach (var _ in tran.Run(circuit.Circuit, Transient.ExportTransient))
            {
                var k = (long)Math.Round(tran.Time / step);
                if (k < result.Length)
                {
                    result[k] = voltage.Value;
                    written++;
                }
            }

            Assert.Equal(result.Length, written);
            return (result, tran.Steps);
        }
        finally
        {
            source.Parameters.Waveform = saved;
        }
    }

    private static double Peak(double[] samples) => samples[(samples.Length / 2)..].Max(Math.Abs);

    private static double Median(List<double> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    [Fact]
    public void OneIterationLoopTakesOneIterationPerSampleAndMatchesRender()
    {
        var input = Sine(Fs / 5);
        var (one, steps) = RenderOneIteration(Load(), input);
        var reference = Analyses.Render(Load(), input, Fs, 1);

        Assert.Equal(input.Length - 1, steps);
        Assert.All(one, v => Assert.True(double.IsFinite(v)));
        var peakError = Math.Abs(Peak(one) - Peak(reference)) / Peak(reference);
        var maxDifference = one.Zip(reference, (a, b) => Math.Abs(a - b)).Max();
        Assert.True(peakError < 0.01, $"peak error {peakError:P3}");
        output.WriteLine($"peak one-iteration {Peak(one):F6} V, Render {Peak(reference):F6} V, peak error {peakError * 100:F4} %, max sample difference {maxDifference * 1000:F3} mV");
    }

    [Fact]
    public void MeasureRealTimeFactor()
    {
        const int Seconds = 10;
        var input = Sine(Fs * Seconds);
        _ = RenderOneIteration(Load(), input);
        _ = Analyses.Render(Load(), input, Fs, 1);

        var one = new List<double>();
        var render = new List<double>();
        for (var run = 0; run < Runs; run++)
        {
            var circuit = Load();
            var sw = Stopwatch.StartNew();
            _ = RenderOneIteration(circuit, input);
            one.Add(Seconds / sw.Elapsed.TotalSeconds);

            circuit = Load();
            sw.Restart();
            _ = Analyses.Render(circuit, input, Fs, 1);
            render.Add(Seconds / sw.Elapsed.TotalSeconds);
        }

        output.WriteLine($"one-iteration loop: {Median(one):F1}x ({one.Min():F1} to {one.Max():F1}); Analyses.Render: {Median(render):F1}x ({render.Min():F1} to {render.Max():F1}); {Runs} runs, {Seconds} s of audio, warm");
    }
}
