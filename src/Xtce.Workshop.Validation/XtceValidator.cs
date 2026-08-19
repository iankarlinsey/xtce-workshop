using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// Runs every registered rule against a document (a SpaceSystem tree), depth-first,
/// building a "/"-joined path of SpaceSystem names for each finding's Location.
/// </summary>
public static class XtceValidator
{
    private static readonly IReadOnlyList<IValidationRule> Rules =
    [
        new EnumInitialValueMustBeValidLabelRule(),
        new TypedValueValidForTypeRule(),
    ];

    public static IReadOnlyList<ValidationIssue> Validate(SpaceSystem root)
    {
        var issues = new List<ValidationIssue>();
        Walk(root, root.Name, issues);
        return issues;
    }

    private static void Walk(SpaceSystem node, string path, List<ValidationIssue> issues)
    {
        foreach (var rule in Rules)
        {
            issues.AddRange(rule.ValidateSpaceSystem(node, path));
        }

        foreach (var child in node.Children)
        {
            Walk(child, $"{path}/{child.Name}", issues);
        }
    }
}
