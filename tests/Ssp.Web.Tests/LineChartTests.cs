using System.Text.RegularExpressions;
using Bunit;
using Ssp.Web.Components;

namespace Ssp.Web.Tests;

public class LineChartTests : BunitContext
{
    IRenderedComponent<LineChart> Chart(int n, bool logX = false) => Render<LineChart>(p => p
        .Add(c => c.X, Enumerable.Range(1, n).Select(i => i * 10.0).ToList())
        .Add(c => c.Series, new Dictionary<string, IReadOnlyList<double>> { ["out"] = Enumerable.Range(0, n).Select(i => -i * 0.5).ToList() })
        .Add(c => c.LogX, logX)
        .Add(c => c.XLabel, "Frequency")
        .Add(c => c.XUnit, "Hz")
        .Add(c => c.YLabel, "Magnitude")
        .Add(c => c.YUnit, "dB"));

    static int Vertices(string d) => Regex.Matches(d, "[ML]").Count;

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(61)]
    public void OnePathWithNVertices(int n)
    {
        var chart = Chart(n);

        var path = Assert.Single(chart.FindAll("path.series"));
        Assert.Equal(n, Vertices(path.GetAttribute("d")!));
    }

    [Fact]
    public void EachAxisLabelHasAUnit()
    {
        var chart = Chart(5);

        var labels = chart.FindAll("text.axis-label").Select(t => t.TextContent).ToList();
        Assert.Equal(["Frequency (Hz)", "Magnitude (dB)"], labels);
    }

    [Fact]
    public void LogAxisHasDecadeTicks()
    {
        // NOTE: X runs from 10 to 1000, so the decades are 10, 100 and 1k.
        var chart = Chart(100, logX: true);

        var ticks = chart.FindAll("text.tick.x").Select(t => t.TextContent).ToList();
        Assert.Equal(["10", "100", "1k"], ticks);
    }

    [Fact]
    public void PointsStayInsideThePlotArea()
    {
        var chart = Chart(20, logX: true);

        var numbers = Regex.Matches(chart.Find("path.series").GetAttribute("d")!, @"-?\d+(\.\d+)?")
            .Select(m => double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        Assert.All(numbers, v => Assert.InRange(v, 0, LineChart.Width));
    }
}
