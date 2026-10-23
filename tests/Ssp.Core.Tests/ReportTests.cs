using System.Text.RegularExpressions;

namespace Ssp.Core.Tests;

public class ReportTests
{
    static string RcLowpassReport() => Report.Render(Runner.Run(Fixtures.Read("rc-lowpass.cir"), new RunOptions()));

    // NOTE: A number starts after a space, a bracket or a sign, so digits in names such as V1 are not numbers.
    static readonly Regex Number = new(@"(?<![\p{L}\d.])[-+]?\d+(?:\.\d+)?", RegexOptions.CultureInvariant);
    static readonly Regex Unit = new(@"\G ?[fpnµmkMG]?(?:V|A|W|Hz|dB|Ω|°)(?![\p{L}\d])", RegexOptions.CultureInvariant);

    [Fact]
    public void RcLowpassReportEqualsGoldenText()
    {
        Golden.Assert("report-rc-lowpass", RcLowpassReport());
    }

    [Fact]
    public void EveryNumberHasAUnit()
    {
        var report = RcLowpassReport();

        var numbers = Number.Matches(report);
        Assert.NotEmpty(numbers);
        foreach (Match number in numbers)
        {
            var line = report[(report.LastIndexOf('\n', number.Index) + 1)..].Split('\n')[0];
            Assert.True(Unit.IsMatch(report, number.Index + number.Length), $"The number {number.Value} has no unit: \"{line}\"");
        }
    }

    [Fact]
    public void ReportHasTheFiveSectionsInOrder()
    {
        var headings = RcLowpassReport().Split('\n').Where(l => l.Length > 0 && !char.IsWhiteSpace(l[0])).ToList();

        Assert.Equal(["Summary", "Voltages", "Response", "Impedances", "Diagnostics"], headings);
    }

    [Fact]
    public void ASummaryDescribesTheOutputNodeOfABandPassCircuit()
    {
        var summary = SummaryOf(Report.Render(Runner.Run(Fixtures.Read("ce-boost.cir"), new RunOptions())));

        Assert.Contains("Node out peaks at", summary);
        Assert.Contains("Node out is 3 dB below its peak at", summary);
        Assert.DoesNotContain("Node e ", summary);
    }

    [Fact]
    public void ALowPassSummaryKeepsTheCornerOfTheOutputNode()
    {
        var summary = SummaryOf(RcLowpassReport());

        Assert.Contains("Node out is 3 dB below its peak at 1.59 kHz.", summary);
    }

    [Fact]
    public void OneErrorUsesTheSingularVerb()
    {
        var summary = SummaryOf(Report.Render(Runner.Run(Fixtures.Read("diag-no-ground.cir"), new RunOptions())));

        Assert.Contains("There is 1 error and no warnings.", summary);
    }

    static string SummaryOf(string report) => report[..report.IndexOf("\nVoltages", StringComparison.Ordinal)];
}
