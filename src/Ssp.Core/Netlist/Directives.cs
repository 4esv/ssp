namespace Ssp.Core.Netlist;

public sealed record Directives(
    string? Title,
    string? Input,
    string? Output,
    IReadOnlyList<KnobDirective> Knobs,
    IReadOnlyList<PartDirective> Parts,
    IReadOnlyList<SwitchDirective> Switches);

public sealed record KnobDirective(string Part, string Taper, double Position);

/// <summary>A switch: the throw that is closed, 1 to the throw count. 0 closes none.</summary>
public sealed record SwitchDirective(string Part, int Position);

public sealed record PartDirective(string Reference, string PartId);

public sealed record DirectiveResult(Directives Directives, IReadOnlyList<Diagnostic> Diagnostics);
