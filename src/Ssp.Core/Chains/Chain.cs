using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Ssp.Core.Netlist;

namespace Ssp.Core.Chains;

/// <summary>A circuit block in a chain. The netlist needs <c>ssp:input</c> and <c>ssp:output</c> directives.</summary>
public sealed record ChainStage(string Name, string Netlist);

/// <summary>
/// Joins circuit blocks in series into one netlist.
/// Each stage becomes a subcircuit with the instance <c>X&lt;stage&gt;</c>, so its parts load as <c>X&lt;stage&gt;.&lt;ref&gt;</c>
/// and its nodes as <c>X&lt;stage&gt;.&lt;node&gt;</c>. The output of each stage drives the input of the next stage.
/// </summary>
public static partial class Chain
{
    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex StageName();

    /// <summary>
    /// Returns one netlist for the stages in series. A voltage source from a node to ground that is not the input source
    /// is a supply. The supply nodes are shared, and the first source for each supply node stays.
    /// A stage whose input source has a DC value that is not 0 is a power stage. If the chain has a stage that is not a power
    /// stage, each power stage keeps its input source as a supply and is not in the signal path.
    /// The input source of the first stage in the signal path drives the chain.
    /// Each <c>knob</c> directive is named <c>&lt;stage&gt;.&lt;part&gt;</c>.
    /// </summary>
    /// <exception cref="ArgumentException">A stage name is bad or not unique, a stage has no input or output,
    /// or two stages have different definitions with the same name.</exception>
    public static string Compose(IReadOnlyList<ChainStage> stages)
    {
        if (stages.Count == 0)
        {
            throw new ArgumentException("A chain needs one or more stages.", nameof(stages));
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stage in stages)
        {
            if (!StageName().IsMatch(stage.Name ?? ""))
                throw new ArgumentException($"Stage name '{stage.Name}' must have only letters, digits and '_'.", nameof(stages));
            if (!names.Add(stage.Name!))
                throw new ArgumentException($"Stage name '{stage.Name}' is not unique.", nameof(stages));
        }

        var definitions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<string>();
        var supplies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bodies = new StringBuilder();
        var knobs = new List<string>();
        string? chainInput = null, node = null;

        // NOTE: power-9v.cir has the 9 V adapter as its input source. In series it put the 47 ohm and 100u filter in the
        // signal path, and it took the chain output, so the chain was silent (#155).
        var powers = stages.Select(IsPower).ToList();
        if (powers.All(p => p)) powers = [.. powers.Select(_ => false)];

        foreach (var (stage, power) in stages.Zip(powers))
        {
            var directives = DirectiveParser.Parse(stage.Netlist).Directives;
            var input = directives.Input ?? throw new ArgumentException($"Stage {stage.Name} has no ssp:input directive.", nameof(stages));
            var output = directives.Output ?? throw new ArgumentException($"Stage {stage.Name} has no ssp:output directive.", nameof(stages));
            var instance = "X" + stage.Name;
            var first = !power && chainInput is null;
            if (first)
            {
                chainInput = $"{instance}.{input}";
                node = chainInput;
            }

            var parts = new List<string>();
            var pins = new List<string>();
            foreach (var line in Statements(stage.Netlist, stage.Name, definitions))
            {
                var words = Words(line);
                if (IsSourceToGround(words, out var top))
                {
                    if (!power && top.Equals(input, StringComparison.OrdinalIgnoreCase))
                    {
                        if (first)
                            sources.Add(Join([$"V{stage.Name}.{words[0]}", .. words[1..3].Select(w => w == top ? chainInput! : w), .. words[3..]]));
                        continue;
                    }

                    if (supplies.Add(top))
                        sources.Add(Join([$"V{stage.Name}.{words[0]}", .. words[1..]]));
                    if (!pins.Contains(top, StringComparer.OrdinalIgnoreCase))
                        pins.Add(top);
                    continue;
                }

                parts.Add(line);
            }

            var next = $"{instance}.{output}";
            bodies.AppendLine($".subckt STAGE_{stage.Name} {Join(power ? [output, .. pins] : [input, output, .. pins])}");
            foreach (var part in parts) bodies.AppendLine(part);
            bodies.AppendLine($".ends STAGE_{stage.Name}");
            bodies.AppendLine($"{instance} {Join(power ? [next, .. pins] : [node!, next, .. pins])} STAGE_{stage.Name}");
            if (!power) node = next;

            knobs.AddRange(directives.Knobs.Select(k =>
                $"* ssp:knob {stage.Name}.{k.Part} {k.Taper} {k.Position.ToString("R", CultureInfo.InvariantCulture)}"));
        }

        var netlist = new StringBuilder();
        netlist.AppendLine($"* ssp:title Chain: {string.Join(", ", stages.Select(s => s.Name))}");
        netlist.AppendLine($"* ssp:input {chainInput}");
        netlist.AppendLine($"* ssp:output {node}");
        foreach (var knob in knobs) netlist.AppendLine(knob);
        netlist.AppendLine();
        foreach (var definition in definitions.Values) netlist.AppendLine(definition);
        foreach (var source in sources) netlist.AppendLine(source);
        netlist.Append(bodies);
        netlist.AppendLine(".END");
        return netlist.ToString();
    }

    // Returns the part lines of a stage. Moves each .subckt and .model definition and each other dot line to the definitions.
    private static IEnumerable<string> Statements(string netlist, string stage, Dictionary<string, string> definitions)
    {
        var lines = Lines(netlist).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || line.StartsWith('*'))
            {
                continue;
            }

            var keyword = words[0].ToLowerInvariant();
            if (keyword == ".end")
            {
                yield break;
            }

            if (!keyword.StartsWith('.'))
            {
                yield return line;
                continue;
            }

            var text = line;
            if (keyword == ".subckt")
            {
                var end = lines.FindIndex(i, l => l.StartsWith(".ends", StringComparison.OrdinalIgnoreCase));
                if (end < 0) throw new ArgumentException($"Stage {stage}: .subckt {words.ElementAtOrDefault(1)} has no .ends.");
                text = string.Join("\n", lines.Where((_, n) => n >= i && n <= end && !lines[n].StartsWith('*')));
                i = end;
            }

            var key = keyword is ".subckt" or ".model" && words.Length > 1 ? $"{keyword} {words[1]}" : text;
            if (definitions.TryGetValue(key, out var existing) && existing != text)
            {
                throw new ArgumentException($"Stage {stage}: {key} differs from the definition of an earlier stage.");
            }
            definitions[key] = text;
        }
    }

    // Joins each '+' continuation line to the line before it.
    private static IEnumerable<string> Lines(string netlist)
    {
        string? current = null;
        foreach (var raw in netlist.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('+') && current is not null)
            {
                current += " " + line[1..].Trim();
                continue;
            }
            if (current is not null) yield return current;
            current = line;
        }
        if (current is not null) yield return current;
    }

    // A power stage has an input source with a DC value that is not 0, such as the 9 V adapter.
    private static bool IsPower(ChainStage stage)
    {
        var input = DirectiveParser.Parse(stage.Netlist).Directives.Input;
        return Lines(stage.Netlist).Select(Words).Any(w =>
            IsSourceToGround(w, out var top) && top.Equals(input, StringComparison.OrdinalIgnoreCase) && !IsZero(DcValue(w)));
    }

    private static string[] Words(string line) => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static bool IsSourceToGround(string[] words, out string node)
    {
        node = "";
        if (words.Length < 3 || !(words[0].StartsWith('V') || words[0].StartsWith('v')))
        {
            return false;
        }

        if (IsGround(words[2])) node = words[1];
        else if (IsGround(words[1])) node = words[2];
        return node.Length > 0 && !IsGround(node);
    }

    // Returns the DC value of a source: the word after DC, or the first value word. A source with no DC value is at 0.
    private static string DcValue(string[] words)
    {
        var dc = Array.FindIndex(words, 3, w => w.Equals("dc", StringComparison.OrdinalIgnoreCase));
        if (dc >= 0) return words.ElementAtOrDefault(dc + 1) ?? "0";
        return words.Length > 3 && (char.IsDigit(words[3][0]) || words[3][0] is '.' or '-' or '+') ? words[3] : "0";
    }

    // A SPICE value with any unit suffix. A value that is not a number, such as an expression, is not 0.
    private static bool IsZero(string value) =>
        double.TryParse(value[..(value.Length - value.Reverse().TakeWhile(char.IsLetter).Count())], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n == 0;

    private static bool IsGround(string node) => node == "0" || node.Equals("gnd", StringComparison.OrdinalIgnoreCase);

    private static string Join(IEnumerable<string> words) => string.Join(" ", words);
}
