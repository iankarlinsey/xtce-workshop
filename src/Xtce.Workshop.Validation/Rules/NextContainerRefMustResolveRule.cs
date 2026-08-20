namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R10: a RestrictionCriteria's NextContainer reference must resolve to an
/// existing container (RestrictionCriteriaType/NextContainer, XSD line 817). Mechanically a
/// dangling-reference check like R11, but kept as its own rule to match the matrix's rule
/// identity — R11 deliberately excludes this site.
/// </summary>
public sealed class NextContainerRefMustResolveRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R10-nextcontainer-ref-must-resolve";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var container in context.Node.TelemetryMetaData?.ContainerSet ?? [])
        {
            if (container.BaseContainer?.RestrictionCriteria?.NextContainerRef is { } nextContainerRef
                && !NameReferenceResolver.Resolve(context, nextContainerRef, NamedItemKind.Container).Found)
            {
                yield return new ValidationIssue(
                    RuleId,
                    Severity,
                    $"{context.Path}/ContainerSet/{container.Name}",
                    $"NextContainer containerRef '{nextContainerRef}' does not resolve to any container.",
                    CandidateNumber: 19);
            }
        }
    }
}
