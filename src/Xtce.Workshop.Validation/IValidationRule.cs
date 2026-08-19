namespace Xtce.Workshop.Validation;

/// <summary>
/// One rule from research/xtce-1.2-rule-matrix.csv. The engine (XtceValidator) builds a
/// SpaceSystemContext index over the document once and calls each rule once per SpaceSystem
/// node — rules don't recurse themselves, and they reach cross-system information (name
/// resolution, ancestors) through the context rather than re-walking the tree.
/// </summary>
public interface IValidationRule
{
    string RuleId { get; }
    ValidationSeverity Severity { get; }
    IEnumerable<ValidationIssue> Validate(SpaceSystemContext context);
}
