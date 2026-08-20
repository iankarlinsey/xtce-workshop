using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R11: a name reference must resolve to an existing named item ("it is illegal
/// for a name reference to point to no item — a dangling name reference", NameReferenceType,
/// XSD line 5013). PARTIAL by construction: only the ref sites the object model represents
/// are checked — Parameter.parameterTypeRef, ParameterRefEntry/ContainerRefEntry refs,
/// BaseContainer.containerRef, and Comparison.parameterRef. References inside preserved raw
/// fragments are invisible. The RestrictionCriteria/NextContainer site is deliberately
/// excluded here: that's rule R10's identity in the matrix (NextContainerRefMustResolveRule).
///
/// Names defined only in preserved content still count as existing (see SpaceSystemContext),
/// so a reference to an unmodeled-but-present item is never flagged.
/// </summary>
public sealed class NoDanglingNameReferencesRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R11-no-dangling-name-references";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var metaCommand in context.Node.CommandMetaData?.MetaCommands ?? [])
        {
            if (metaCommand.BaseMetaCommandRef is { } baseRef
                && !NameReferenceResolver.Resolve(context, baseRef, NamedItemKind.MetaCommand).Found)
            {
                yield return Issue(
                    $"{context.Path}/CommandMetaData/MetaCommandSet/{metaCommand.Name}",
                    $"BaseMetaCommand metaCommandRef '{baseRef}' does not resolve to any MetaCommand.");
            }
        }

        if (context.Node.TelemetryMetaData is not { } telemetry)
        {
            yield break;
        }

        foreach (var parameter in telemetry.ParameterSet)
        {
            if (!NameReferenceResolver.Resolve(context, parameter.ParameterTypeRef, NamedItemKind.ParameterType).Found)
            {
                yield return Issue(
                    $"{context.Path}/ParameterSet/{parameter.Name}",
                    $"parameterTypeRef '{parameter.ParameterTypeRef}' does not resolve to any parameter type.");
            }
        }

        foreach (var type in telemetry.ParameterTypeSet)
        {
            var typePath = $"{context.Path}/ParameterTypeSet/{type.Name}";

            if (type.Kind == ParameterTypeKind.Array && type.ArrayTypeRef is { } arrayTypeRef
                && !NameReferenceResolver.Resolve(context, arrayTypeRef, NamedItemKind.ParameterType).Found)
            {
                yield return Issue(typePath,
                    $"arrayTypeRef '{arrayTypeRef}' does not resolve to any parameter type.");
            }

            foreach (var member in type.Members ?? [])
            {
                if (!NameReferenceResolver.Resolve(context, member.TypeRef, NamedItemKind.ParameterType).Found)
                {
                    yield return Issue(typePath,
                        $"Member '{member.Name}' typeRef '{member.TypeRef}' does not resolve to any parameter type.");
                }
            }
        }

        foreach (var container in telemetry.ContainerSet ?? [])
        {
            var containerPath = $"{context.Path}/ContainerSet/{container.Name}";

            foreach (var entry in container.EntryList)
            {
                switch (entry.Kind)
                {
                    case SequenceEntryKind.ParameterRef
                        when !NameReferenceResolver.Resolve(context, entry.Ref!, NamedItemKind.Parameter).Found:
                        yield return Issue(containerPath,
                            $"ParameterRefEntry parameterRef '{entry.Ref}' does not resolve to any parameter.");
                        break;
                    case SequenceEntryKind.ContainerRef
                        when !NameReferenceResolver.Resolve(context, entry.Ref!, NamedItemKind.Container).Found:
                        yield return Issue(containerPath,
                            $"ContainerRefEntry containerRef '{entry.Ref}' does not resolve to any container.");
                        break;
                }
            }

            if (container.BaseContainer is { } baseContainer)
            {
                if (!NameReferenceResolver.Resolve(context, baseContainer.ContainerRef, NamedItemKind.Container).Found)
                {
                    yield return Issue(containerPath,
                        $"BaseContainer containerRef '{baseContainer.ContainerRef}' does not resolve to any container.");
                }

                var comparisons = new List<Comparison>();
                if (baseContainer.RestrictionCriteria?.Comparison is { } single)
                {
                    comparisons.Add(single);
                }
                comparisons.AddRange(baseContainer.RestrictionCriteria?.ComparisonList ?? []);

                foreach (var comparison in comparisons)
                {
                    if (!NameReferenceResolver.Resolve(context, comparison.ParameterRef, NamedItemKind.Parameter).Found)
                    {
                        yield return Issue(containerPath,
                            $"Comparison parameterRef '{comparison.ParameterRef}' does not resolve to any parameter.");
                    }
                }
            }
        }
    }

    private ValidationIssue Issue(string location, string message) =>
        new(RuleId, Severity, location, message, CandidateNumber: 91);
}
