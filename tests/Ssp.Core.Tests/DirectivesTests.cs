using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class DirectivesTests
{
    [Fact]
    public void ReadsTitle()
    {
        var result = DirectiveParser.Parse("* ssp:title RC low-pass\nR1 a b 1k\n.END\n");

        Assert.Equal("RC low-pass", result.Directives.Title);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ReadsInput()
    {
        var result = DirectiveParser.Parse("* ssp:input in\nR1 in out 1k\n.END\n");

        Assert.Equal("in", result.Directives.Input);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ReadsOutput()
    {
        var result = DirectiveParser.Parse("* ssp:output out\nR1 in out 1k\n.END\n");

        Assert.Equal("out", result.Directives.Output);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ReadsKnob()
    {
        var result = DirectiveParser.Parse("* ssp:knob RV1 audio 0.25\nR1 in out 1k\n.END\n");

        var knob = Assert.Single(result.Directives.Knobs);
        Assert.Equal(new KnobDirective("RV1", "audio", 0.25), knob);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ReadsPart()
    {
        var result = DirectiveParser.Parse("* ssp:part Q1 2n3904\nR1 in out 1k\n.END\n");

        var part = Assert.Single(result.Directives.Parts);
        Assert.Equal(new PartDirective("Q1", "2n3904"), part);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UnknownDirectiveGivesOneWarning()
    {
        var result = DirectiveParser.Parse("* ssp:foo\nR1 in out 1k\n.END\n");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(Severity.Warning, diagnostic.Severity);
        Assert.Equal(1, diagnostic.Line);
    }

    [Fact]
    public void MalformedKnobGivesOneWarningAndNoKnob()
    {
        var result = DirectiveParser.Parse("* ssp:knob RV1 audio high\n.END\n");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(Severity.Warning, diagnostic.Severity);
        Assert.Empty(result.Directives.Knobs);
    }

    [Fact]
    public void OrdinaryCommentsAreIgnored()
    {
        var result = DirectiveParser.Parse("* a comment about ssp: things\n.END\n");

        Assert.Empty(result.Diagnostics);
        Assert.Null(result.Directives.Title);
    }

    [Fact]
    public void RcLowpassWithDirectivesLoadsWithSameEntityCount()
    {
        var plain = Fixtures.Read("rc-lowpass.cir");
        var withDirectives = "* ssp:input in\n* ssp:output out\n* ssp:knob R1 linear 0.5\n* ssp:part C1 film-100n\n" + plain;

        var before = NetlistLoader.Load(plain);
        var after = NetlistLoader.Load(withDirectives);

        Assert.Empty(after.Diagnostics);
        Assert.Equal(before.Circuit.Count, after.Circuit.Count);
        Assert.Equal("in", DirectiveParser.Parse(withDirectives).Directives.Input);
    }
}
