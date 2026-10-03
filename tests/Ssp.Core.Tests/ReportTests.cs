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
}
