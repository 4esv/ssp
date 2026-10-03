using SpiceSharp;
using SpiceSharp.Components;
using SpiceSharpParser;
using SpiceSharpParser.Models.Netlist.Spice.Objects.Parameters;
using ParsedComponent = SpiceSharpParser.Models.Netlist.Spice.Objects.Component;

namespace Ssp.Core.Netlist;

public static class NetlistLoader
{
    public static LoadedCircuit Load(string netlist)
    {
        var diagnostics = new List<Diagnostic>();
        var circuit = new Circuit();
        var subcircuits = new List<SubcircuitInstance>();
        try
        {
            var parsed = new SpiceNetlistParser().ParseNetlist(netlist);
            foreach (var error in parsed.ValidationResult.Errors)
            {
                diagnostics.Add(new Diagnostic(Severity.Error, error.Message, error.LineInfo?.LineNumber));
            }

            if (diagnostics.Count == 0)
            {
                var model = new SpiceSharpReader().Read(parsed.FinalModel);
                circuit = model.Circuit;
                BindModels(parsed.FinalModel.Statements.OfType<ParsedComponent>(), circuit);
                subcircuits.AddRange(Subcircuits(parsed.FinalModel.Statements.OfType<ParsedComponent>()));
                foreach (var error in model.ValidationResult.Errors)
                {
                    diagnostics.Add(new Diagnostic(Severity.Error, error.Message, error.LineInfo?.LineNumber));
                }
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(new Diagnostic(Severity.Error, ex.Message, null));
        }

        var nodes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entity in circuit)
        {
            if (entity is IComponent component)
            {
                nodes.UnionWith(component.Nodes);
            }
        }

        var directives = DirectiveParser.Parse(netlist);

        return new LoadedCircuit(circuit, nodes, diagnostics, directives.Directives, subcircuits);
    }

    // NOTE: the reader flattens each X line into the parts of its subcircuit. Keep the X line, so a rule can find the pins of the instance.
    private static IEnumerable<SubcircuitInstance> Subcircuits(IEnumerable<ParsedComponent> components)
    {
        foreach (var x in components.Where(c => c.Name.StartsWith('X') || c.Name.StartsWith('x')))
        {
            var words = x.PinsAndParameters.OfType<SingleParameter>().Select(p => p.Value)
                .TakeWhile(v => !v.Equals("params:", StringComparison.OrdinalIgnoreCase)).ToList();
            if (words.Count >= 2)
            {
                yield return new SubcircuitInstance(x.Name, words[^1], words[..^1]);
            }
        }
    }

    // NOTE: the parser leaves BipolarJunctionTransistor.Model and Diode.Model unset, so a BJT simulation throws. Bind them from the netlist.
    private static void BindModels(IEnumerable<ParsedComponent> components, Circuit circuit)
    {
        var byName = components.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var bjt in circuit.OfType<BipolarJunctionTransistor>())
        {
            if (string.IsNullOrEmpty(bjt.Model) && byName.TryGetValue(bjt.Name, out var statement))
            {
                bjt.Model = statement.PinsAndParameters[statement.PinsAndParameters.Count - 1].Value;
            }
        }
        foreach (var diode in circuit.OfType<Diode>())
        {
            if (string.IsNullOrEmpty(diode.Model) && byName.TryGetValue(diode.Name, out var statement))
            {
                diode.Model = statement.PinsAndParameters[statement.PinsAndParameters.Count - 1].Value;
            }
        }
    }
}
