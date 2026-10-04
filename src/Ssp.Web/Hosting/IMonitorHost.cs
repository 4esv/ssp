namespace Ssp.Web.Hosting;

/// <summary>
/// The host side of the live monitor. One session runs at a time and keeps its solver state between chunks.
/// </summary>
public interface IMonitorHost
{
    /// <summary>Starts a session on the netlist. The netlist needs an <c>ssp:input</c> node.</summary>
    Task MonitorStart(string netlist, int sampleRate);

    /// <summary>Gives one input chunk to the session and returns the output chunk of the same length.</summary>
    Task<double[]> MonitorProcess(double[] chunk);

    /// <summary>Ends the session.</summary>
    Task MonitorStop();
}
