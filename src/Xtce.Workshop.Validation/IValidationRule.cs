using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// One rule from research/xtce-1.2-rule-matrix.csv, implemented against a single SpaceSystem
/// node's own content. The engine (XtceValidator) walks the tree and calls this once per
/// node — rules don't need to handle recursion themselves.
/// </summary>
public interface IValidationRule
{
    string RuleId { get; }
    ValidationSeverity Severity { get; }
    IEnumerable<ValidationIssue> ValidateSpaceSystem(SpaceSystem spaceSystem, string path);
}
