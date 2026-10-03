using System.Globalization;

namespace Ssp.Core.Netlist;

/// <summary>
/// Reads the <c>* ssp:</c> comment lines of a netlist. Other SPICE tools read these lines as comments.
/// </summary>
public static class DirectiveParser
{
    private const string Prefix = "* ssp:";

    public static DirectiveResult Parse(string netlist)
    {
        string? title = null, input = null, output = null;
        var knobs = new List<KnobDirective>();
        var parts = new List<PartDirective>();
        var diagnostics = new List<Diagnostic>();

        var lines = netlist.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var number = i + 1;
            var body = line[Prefix.Length..];
            var words = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var name = words.Length > 0 ? words[0] : "";
            var args = words.Skip(1).ToArray();

            switch (name)
            {
                case "title":
                    var text = body.Trim()[name.Length..].Trim();
                    if (text.Length == 0) Warn("ssp:title needs a text.");
                    else title = text;
                    break;
                case "input":
                    if (args.Length == 1) input = args[0];
                    else Warn("ssp:input needs one node.");
                    break;
                case "output":
                    if (args.Length == 1) output = args[0];
                    else Warn("ssp:output needs one node.");
                    break;
                case "knob":
                    if (args.Length == 3 && double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var pos))
                        knobs.Add(new KnobDirective(args[0], args[1], pos));
                    else Warn("ssp:knob needs <part> <taper> <pos>, where pos is a number.");
                    break;
                case "part":
                    if (args.Length == 2) parts.Add(new PartDirective(args[0], args[1]));
                    else Warn("ssp:part needs <ref> <part-id>.");
                    break;
                default:
                    Warn($"Unknown directive 'ssp:{name}'.");
                    break;
            }

            void Warn(string message) => diagnostics.Add(new Diagnostic(Severity.Warning, message, number));
        }

        return new DirectiveResult(new Directives(title, input, output, knobs, parts), diagnostics);
    }
}
