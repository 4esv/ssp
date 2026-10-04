using SpiceSharp;
using SpiceSharp.Components;
using SpiceSharpParser;
using SpiceSharpParser.Models.Netlist.Spice.Objects;
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
                BindModels(parsed.FinalModel.Statements, circuit);
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
        foreach (var x in components.Where(IsInstance))
        {
            var words = Words(x);
            if (words.Count >= 2)
            {
                yield return new SubcircuitInstance(x.Name, words[^1], words[..^1]);
            }
        }
    }

    private static bool IsInstance(ParsedComponent c) => c.Name.StartsWith('X') || c.Name.StartsWith('x');

    private static List<string> Words(ParsedComponent x) =>
        x.PinsAndParameters.OfType<SingleParameter>().Select(p => p.Value)
            .TakeWhile(v => !v.Equals("params:", StringComparison.OrdinalIgnoreCase)).ToList();

    // NOTE: the parser leaves BipolarJunctionTransistor.Model and Diode.Model unset, so a BJT simulation throws. Bind them from the netlist.
    // A part inside a subcircuit is flattened to X1.D1, and a .model inside the subcircuit to X1.NAME, so walk the X lines to find both.
    private static void BindModels(IEnumerable<Statement> statements, Circuit circuit)
    {
        var parts = new Dictionary<string, (ParsedComponent Statement, string Scope)>(StringComparer.OrdinalIgnoreCase);
        Collect(statements.ToList(), "", [], new HashSet<SubCircuit>(), parts);
        foreach (var bjt in circuit.OfType<BipolarJunctionTransistor>())
        {
            if (string.IsNullOrEmpty(bjt.Model) && parts.TryGetValue(bjt.Name, out var part))
            {
                bjt.Model = ModelName(part.Statement, part.Scope, circuit);
            }
        }
        foreach (var diode in circuit.OfType<Diode>())
        {
            if (string.IsNullOrEmpty(diode.Model) && parts.TryGetValue(diode.Name, out var part))
            {
                diode.Model = ModelName(part.Statement, part.Scope, circuit);
            }
        }
    }

    private static void Collect(
        IReadOnlyList<Statement> statements,
        string scope,
        IReadOnlyList<IReadOnlyDictionary<string, SubCircuit>> outer,
        IReadOnlySet<SubCircuit> active,
        Dictionary<string, (ParsedComponent Statement, string Scope)> parts)
    {
        var local = statements.OfType<SubCircuit>().GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<IReadOnlyDictionary<string, SubCircuit>> scopes = [local, .. outer];
        foreach (var component in statements.OfType<ParsedComponent>())
        {
            parts[scope + component.Name] = (component, scope);
            if (!IsInstance(component) || Words(component) is not [.., var name])
            {
                continue;
            }

            var definition = scopes.Select(d => d.GetValueOrDefault(name)).FirstOrDefault(d => d is not null);
            if (definition is not null && !active.Contains(definition))
            {
                Collect(definition.Statements.ToList(), scope + component.Name + ".", scopes, new HashSet<SubCircuit>(active) { definition }, parts);
            }
        }
    }

    // NOTE: the innermost .model wins. X1.X2.NAME, then X1.NAME, then NAME.
    private static string ModelName(ParsedComponent statement, string scope, Circuit circuit)
    {
        var model = statement.PinsAndParameters[statement.PinsAndParameters.Count - 1].Value;
        while (scope.Length > 0)
        {
            if (circuit.Contains(scope + model))
            {
                return scope + model;
            }

            var dot = scope.LastIndexOf('.', scope.Length - 2);
            scope = dot < 0 ? "" : scope[..(dot + 1)];
        }

        return model;
    }
}
