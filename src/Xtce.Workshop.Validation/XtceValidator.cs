using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// Runs every registered rule against a document (a SpaceSystem tree): builds the
/// SpaceSystemContext index once, then calls each rule for each SpaceSystem node.
/// </summary>
public static class XtceValidator
{
    private static readonly IReadOnlyList<IValidationRule> Rules =
    [
        new EnumInitialValueMustBeValidLabelRule(),
        new TypedValueValidForTypeRule(),
        new NoDanglingNameReferencesRule(),
        new NextContainerRefMustResolveRule(),
        new TimeDataTypeRequiresEncodingRule(),
        new AmbiguousTimeUnitsRule(),
        new LocationInContainerFlagsRule(),
        new ContainerSegmentsNoOverlapRule(),
        new MessageContainerRefMustBeRootRule(),
        new ArrayDimCountMatchTypeRule(),
        new DimSubsetLessThanTypeRule(),
        new DimensionOrderMustAscendRule(),
        new SplineOrderRequiresMinPointsRule(),
        new ChecksumCustomRequiresInputAlgorithmRule(),
    ];

    public static IReadOnlyList<ValidationIssue> Validate(SpaceSystem root)
    {
        var rootContext = SpaceSystemContext.Build(root);
        var issues = new List<ValidationIssue>();

        foreach (var context in rootContext.SelfAndDescendants())
        {
            foreach (var rule in Rules)
            {
                issues.AddRange(rule.Validate(context));
            }
        }

        return issues;
    }
}
