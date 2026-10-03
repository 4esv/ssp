using SpiceSharp;
using SpiceSharp.Components;
using SpiceSharpParser;

namespace Ssp.Core.Netlist;

public static class NetlistLoader
{
    public static LoadedCircuit Load(string netlist)
    {
        var diagnostics = new List<Diagnostic>();
        var circuit = new Circuit();
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

        return new LoadedCircuit(circuit, nodes, diagnostics);
    }
}
