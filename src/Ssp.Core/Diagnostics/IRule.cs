using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

/// <summary>A check on a loaded circuit. A rule never runs a simulation.</summary>
public interface IRule
{
    IEnumerable<Diagnostic> Check(LoadedCircuit circuit);
}
