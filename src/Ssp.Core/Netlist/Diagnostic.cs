namespace Ssp.Core.Netlist;

public enum Severity
{
    Warning,
    Error,
}

public sealed record Diagnostic(Severity Severity, string Message, int? Line);
