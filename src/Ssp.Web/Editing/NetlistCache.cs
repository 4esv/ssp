using Ssp.Core.Netlist;

namespace Ssp.Web.Editing;

/// <summary>
/// The loaded circuit of the last netlist text. The editor page gives one cache to its panels, so an edit loads the
/// netlist once and the schematic, the calculators, the compare panel and the export share the result.
/// </summary>
public sealed class NetlistCache
{
    string? text;
    LoadedCircuit? circuit;

    /// <summary>The number of times the cache loaded a netlist.</summary>
    public int Loads { get; private set; }

    /// <summary>The loaded circuit of the netlist. The same text gives the same object.</summary>
    public LoadedCircuit Load(string netlist)
    {
        if (circuit is null || netlist != text)
        {
            circuit = NetlistLoader.Load(netlist);
            text = netlist;
            Loads++;
        }
        return circuit;
    }
}
