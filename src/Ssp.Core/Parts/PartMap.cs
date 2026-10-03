using Ssp.Core.Netlist;
using SpiceSharp.Components;
using SpiceSharp.Entities;

namespace Ssp.Core.Parts;

/// <summary>
/// The part for each netlist reference. A reference with no part has a null value and a warning.
/// </summary>
public sealed class PartMap
{
    PartMap(IReadOnlyDictionary<string, PartRow?> parts, IReadOnlyList<Diagnostic> diagnostics)
    {
        Parts = parts;
        Diagnostics = diagnostics;
    }

    public IReadOnlyDictionary<string, PartRow?> Parts { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// Uses the <c>* ssp:part</c> directive first. Then uses the part whose kind and model name match the component.
    /// </summary>
    public static PartMap Resolve(LoadedCircuit circuit, PartsTable parts)
    {
        var diagnostics = new List<Diagnostic>();
        var directed = new Dictionary<string, PartRow>(StringComparer.OrdinalIgnoreCase);
        var references = new Dictionary<string, IEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in circuit.Circuit)
        {
            if (entity is IComponent) references[entity.Name] = entity;
        }

        foreach (var d in circuit.Directives.Parts)
        {
            if (!references.ContainsKey(d.Reference))
            {
                diagnostics.Add(new Diagnostic(Severity.Warning, $"ssp:part names reference '{d.Reference}', which is not in the netlist.", null));
            }
            else if (parts.Get(d.PartId) is not { } row)
            {
                diagnostics.Add(new Diagnostic(Severity.Error, $"ssp:part maps '{d.Reference}' to unknown part id '{d.PartId}'.", null));
            }
            else
            {
                directed[d.Reference] = row;
            }
        }

        var map = new Dictionary<string, PartRow?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (reference, entity) in references)
        {
            var row = directed.GetValueOrDefault(reference) ?? Default(entity, parts);
            map[reference] = row;
            if (row is null)
            {
                diagnostics.Add(new Diagnostic(Severity.Warning, $"Reference '{reference}' has no part.", null));
            }
        }
        return new PartMap(map, diagnostics);
    }

    static PartRow? Default(IEntity entity, PartsTable parts)
    {
        var (kinds, model) = entity switch
        {
            Diode d => (new[] { "diode", "led" }, d.Model),
            BipolarJunctionTransistor q => (new[] { "bjt" }, q.Model),
            _ => (Array.Empty<string>(), null),
        };
        if (string.IsNullOrEmpty(model)) return null;
        return parts.Rows.FirstOrDefault(r =>
            kinds.Contains(r.Kind, StringComparer.Ordinal) && string.Equals(r.Model, model, StringComparison.OrdinalIgnoreCase));
    }
}
