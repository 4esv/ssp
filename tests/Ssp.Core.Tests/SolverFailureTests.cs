using SpiceSharp;
using SpiceSharp.Algebra;
using SpiceSharp.Simulations;
using Ssp.Core.Analysis;
using Ssp.Core.Diagnostics;
using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class SolverFailureTests
{
    static LoadedCircuit Load(string fixture) => NetlistLoader.Load(Fixtures.Read(fixture));

    [Fact]
    public void ValidationFailureGivesADiagnosticNamingAPart()
    {
        var run = SolverFailure.OperatingPoint(Load("diag-voltage-loop.cir"));

        Assert.Null(run.Value);
        var diagnostic = Assert.IsType<Diagnostic>(run.Diagnostic);
        Assert.Equal(Severity.Error, diagnostic.Severity);
        Assert.True(diagnostic.Message.Contains("V1") || diagnostic.Message.Contains("V2"), diagnostic.Message);
        Assert.DoesNotContain("SpiceSharp", diagnostic.Message);
    }

    [Fact]
    public void GoodCircuitHasAValueAndNoDiagnostic()
    {
        var run = SolverFailure.OperatingPoint(Load("divider-basic.cir"));

        Assert.NotNull(run.Value);
        Assert.Null(run.Diagnostic);
    }

    [Fact]
    public void RetryHappensOneTimeOnly()
    {
        var calls = new List<Action<BiasingParameters>?>();
        var run = SolverFailure.OperatingPoint(Load("divider-basic.cir"), (_, configure) =>
        {
            calls.Add(configure);
            throw new ValidationFailedException("forced");
        });

        Assert.Equal(2, calls.Count);
        Assert.Null(calls[0]);
        Assert.NotNull(calls[1]);
        Assert.NotNull(run.Diagnostic);
    }

    [Fact]
    public void RetryRaisesTheLimits()
    {
        var defaults = new BiasingParameters();
        BiasingParameters? raised = null;
        SolverFailure.OperatingPoint(Load("divider-basic.cir"), (_, configure) =>
        {
            if (configure is not null)
            {
                raised = new BiasingParameters();
                configure(raised);
            }

            throw new ValidationFailedException("forced");
        });

        Assert.NotNull(raised);
        Assert.True(raised.GminSteps > defaults.GminSteps);
        Assert.True(raised.SourceSteps > defaults.SourceSteps);
        Assert.True(raised.DcMaxIterations > defaults.DcMaxIterations);
    }

    [Fact]
    public void RetryCanSucceed()
    {
        var calls = 0;
        var expected = new OpResult(new Dictionary<string, double>(), new Dictionary<string, double>(), new Dictionary<string, double>());
        var run = SolverFailure.OperatingPoint(Load("divider-basic.cir"), (_, _) =>
        {
            if (++calls == 1)
            {
                throw new ValidationFailedException("first");
            }

            return expected;
        });

        Assert.Equal(2, calls);
        Assert.Same(expected, run.Value);
        Assert.Null(run.Diagnostic);
    }

    [Fact]
    public void OtherExceptionsAreNotCaughtOrRetried()
    {
        var calls = 0;
        Assert.Throws<InvalidOperationException>(() => SolverFailure.OperatingPoint(Load("divider-basic.cir"), (_, _) =>
        {
            calls++;
            throw new InvalidOperationException("bug");
        }));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(typeof(ValidationFailedException))]
    [InlineData(typeof(SingularException))]
    [InlineData(typeof(SpiceSharpException))]
    public void EveryDiagnosticHasANextStep(Type type)
    {
        var exception = (Exception)Activator.CreateInstance(type)!;

        var diagnostic = SolverFailure.ToDiagnostic(exception, Load("divider-basic.cir"));

        Assert.Equal(Severity.Error, diagnostic.Severity);
        Assert.Matches(@"(Check|Add|Connect|Remove|Change|Lower|Raise)\b", diagnostic.Message);
    }
}
