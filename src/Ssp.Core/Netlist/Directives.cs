namespace Ssp.Core.Netlist;

public sealed record Directives(
    string? Title,
    string? Input,
    string? Output,
    IReadOnlyList<KnobDirective> Knobs,
    IReadOnlyList<PartDirective> Parts);

public sealed record KnobDirective(string Part, string Taper, double Position);

public sealed record PartDirective(string Reference, string PartId);

public sealed record DirectiveResult(Directives Directives, IReadOnlyList<Diagnostic> Diagnostics);
