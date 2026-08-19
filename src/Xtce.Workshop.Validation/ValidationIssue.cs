namespace Xtce.Workshop.Validation;

public enum ValidationSeverity
{
    Warning,
    Error,
}

/// <summary>
/// One finding from running a validation rule against a document. `RuleId` matches a row in
/// research/xtce-1.2-rule-matrix.csv, so a finding can always be traced back to the rule
/// definition (and its citation) that produced it. `Location` is a human-readable path built
/// from SpaceSystem names down to the offending construct (e.g.
/// "Mission/Bus/BusState_Type"), not a formal XTCE name reference.
/// </summary>
public sealed record ValidationIssue(string RuleId, ValidationSeverity Severity, string Location, string Message);
