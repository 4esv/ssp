namespace Ssp.Core.Parts;

/// <summary>One row of the parts table.</summary>
public sealed record PartRow(
    string Id,
    string Kind,
    string Model,
    string Symbol,
    string Footprint,
    string BuyUrl,
    string Provenance);
