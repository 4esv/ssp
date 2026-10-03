using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

/// <summary>A check on a loaded circuit. Only a bias rule runs a simulation, and only the operating point.</summary>
public interface IRule
{
    IEnumerable<Diagnostic> Check(LoadedCircuit circuit);
}
