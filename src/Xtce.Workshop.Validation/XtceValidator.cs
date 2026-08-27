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
        new NoDuplicateVerifiersRule(),
        new NoInheritanceCyclesRule(),
        new StringLengthSpecConflictsRule(),
        new TypeInheritanceOverrideRestrictionsRule(),
        new ChangePerSecondRequiresPositiveSpanRule(),
        new TelemeteredParameterRequiresEncodingRule(),
        new CommandContainerInheritanceRule(),
        new FixedValueBitLengthRule(),
        new ConstantDataSourceReadOnlyRule(),
    ];

    /// <summary>Every registered rule's id, in registration order (for the conformance report).</summary>
    public static IReadOnlyList<string> RuleIds => Rules.Select(r => r.RuleId).ToList();

    public static IReadOnlyList<ValidationIssue> Validate(
        SpaceSystem root,
        IProgress<(int RuleIndex, int RuleCount, string RuleId)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var rootContext = SpaceSystemContext.Build(root);
        var issues = new List<ValidationIssue>();

        var contexts = rootContext.SelfAndDescendants().ToList();
        for (var ruleIndex = 0; ruleIndex < Rules.Count; ruleIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((ruleIndex + 1, Rules.Count, Rules[ruleIndex].RuleId));
            foreach (var context in contexts)
            {
                issues.AddRange(Rules[ruleIndex].Validate(context));
            }
        }

        return issues;
    }
}
