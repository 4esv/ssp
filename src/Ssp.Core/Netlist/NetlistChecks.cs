using System.Globalization;
using SpiceSharpParser.Models.Netlist.Spice.Objects;
using SpiceSharpParser.Models.Netlist.Spice.Objects.Parameters;
using ParsedComponent = SpiceSharpParser.Models.Netlist.Spice.Objects.Component;

namespace Ssp.Core.Netlist;

/// <summary>Finds the mistakes in the parsed netlist that the engine reports without a line or a part: a resistor with no value or a value of 0 or less, and a model that no .model line defines.</summary>
public static class NetlistChecks
{
    public static IReadOnlyList<Diagnostic> Run(IEnumerable<Statement> statements)
    {
        var diagnostics = new List<Diagnostic>();
        var all = Flatten(statements).ToList();
        var models = all.OfType<Model>().Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var part in all.OfType<ParsedComponent>())
        {
            var words = part.PinsAndParameters.Select(p => p.Value).ToList();
            var line = part.LineInfo?.LineNumber;
            switch (char.ToUpperInvariant(part.Name[0]))
            {
                case 'R':
                    if (words.Count < 3)
                    {
                        diagnostics.Add(new Diagnostic(Severity.Error, $"{part.Name} on line {line} has no value.", line));
                    }
                    else if (part.PinsAndParameters[2] is SingleParameter && TryNumber(words[2]) is { } value && value <= 0)
                    {
                        diagnostics.Add(new Diagnostic(Severity.Error, $"{part.Name} on line {line} has a value of {words[2]}. A resistor needs a value above 0.", line));
                    }
                    else
                    {
                        CheckUnit(part, words[2], diagnostics);
                    }
                    break;
                case 'C' or 'L' when words.Count >= 3:
                    CheckUnit(part, words[2], diagnostics);
                    break;
                case 'D' when words.Count >= 3:
                    CheckModel(part, words[^1], models, diagnostics);
                    break;
                case 'Q' or 'J' when words.Count >= 4:
                    CheckModel(part, words[^1], models, diagnostics);
                    break;
                case 'M' when words.Count >= 5:
                    CheckModel(part, words[4], models, diagnostics);
                    break;
            }
        }

        return diagnostics;
    }

    // NOTE: #364: the reader ignores letters after the number. A letter that is not a unit is a likely typo.
    static void CheckUnit(ParsedComponent part, string word, List<Diagnostic> diagnostics)
    {
        if (part.PinsAndParameters[2] is not SingleParameter || Overrides.Read(word) is not { Stray.Length: > 0 } read)
        {
            return;
        }

        var line = part.LineInfo?.LineNumber;
        var used = read.Value.ToString("R", CultureInfo.InvariantCulture);
        diagnostics.Add(new Diagnostic(Severity.Warning, $"{part.Name} has the value {word}. ssp reads it as {used}. The letters '{read.Stray}' are not a unit.", line));
    }

    static void CheckModel(ParsedComponent part, string model, HashSet<string> models, List<Diagnostic> diagnostics)
    {
        if (TryNumber(model) is not null || model.Contains('=') || models.Contains(model))
        {
            return;
        }

        diagnostics.Add(new Diagnostic(Severity.Error, $"{part.Name} on line {part.LineInfo?.LineNumber} uses the model {model}. The netlist has no .model line with that name.", part.LineInfo?.LineNumber));
    }

    static double? TryNumber(string text)
    {
        try
        {
            return Overrides.Parse($"X={text}").Value;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    static IEnumerable<Statement> Flatten(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
        {
            yield return statement;
            if (statement is SubCircuit sub)
            {
                foreach (var inner in Flatten(sub.Statements))
                {
                    yield return inner;
                }
            }
        }
    }
}
