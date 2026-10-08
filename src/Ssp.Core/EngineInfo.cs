using System.Reflection;

namespace Ssp.Core;

/// <summary>
/// Reports the version of ssp and of each engine package.
/// </summary>
public static class EngineInfo
{
    /// <summary>The ssp version, without build metadata.</summary>
    public static string ProductVersion => VersionOf(typeof(EngineInfo).Assembly);

    /// <summary>
    /// Returns the name and version of each engine package, in dependency order.
    /// </summary>
    public static IReadOnlyList<(string Package, string Version)> Packages() =>
    [
        ("SpiceSharp", VersionOf(typeof(SpiceSharp.Circuit).Assembly)),
        ("SpiceSharp-Parser", VersionOf(typeof(SpiceSharpParser.SpiceCompiler).Assembly)),
        ("SpiceSharpBehavioral", VersionOf(typeof(SpiceSharp.Components.BehavioralVoltageSource).Assembly)),
        ("SpiceSharpParser.CustomComponents", VersionOf(typeof(SpiceSharpParser.CustomComponents.IdealDiode).Assembly)),
    ];

    /// <summary>
    /// Returns the version text: "ssp &lt;version&gt;", then one "&lt;package&gt; &lt;version&gt;" line per engine package.
    /// </summary>
    public static IReadOnlyList<string> VersionLines() =>
        [$"ssp {ProductVersion}", .. Packages().Select(p => $"{p.Package} {p.Version}")];

    private static string VersionOf(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            // NOTE: strip "+<commit>" build metadata.
            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }

        var version = assembly.GetName().Version;
        return version is null ? "unknown" : version.ToString(3);
    }
}
