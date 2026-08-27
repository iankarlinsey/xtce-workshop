using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R02 (PARTIAL — statically resolvable chains only): an ArrayParameterRefEntry's
/// DimensionList must have the same number of dimensions as the referenced parameter's
/// Array type (ArrayParameterRefEntryType, XSD line 369). The chain is
/// entry.parameterRef → Parameter → parameterTypeRef → ArrayParameterType, each resolved
/// from its own scope; any opaque or unresolvable link skips the check (R11 owns dangling
/// refs).
/// </summary>
public sealed class ArrayDimCountMatchTypeRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R02-array-dim-count-match-type";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var (container, entryFragment, arrayType, _) in ArrayEntryChains.Resolve(context))
        {
            var entryDimensions = XmlFragmentInspector.FindDimensions(entryFragment.OuterXml);
            if (entryDimensions.Count == 0)
            {
                continue; // no DimensionList on the entry — the full array is populated
            }

            var typeCount = arrayType.Dimensions?.Count ?? 0;
            if (entryDimensions.Count != typeCount)
            {
                yield return new ValidationIssue(RuleId, Severity,
                    $"{context.Path}/ContainerSet/{container.Name}",
                    $"ArrayParameterRefEntry has {entryDimensions.Count} dimension(s) but referenced type '{arrayType.Name}' declares {typeCount}.",
                    CandidateNumber: 5);
            }
        }
    }
}

/// <summary>
/// XTCE-1.2-R05 (PARTIAL — FixedValue bounds only): an entry's subsetting dimension bounds
/// must be less than the referenced type's — "it's not a subset if it's the same size"
/// (ArrayParameterRefEntryType/DimensionList, XSD line 379). Flagged: a dimension whose
/// fixed EndingIndex exceeds the type's, and a DimensionList whose every fixed bound
/// equals the type's (same size, not a subset). All three citation sites are checked:
/// telemetry ArrayParameterRefEntry (#6), and command-container
/// ArrayArgumentRefEntry against its ArrayArgumentType (#1) and command-container
/// ArrayParameterRefEntry against the parameter's array type (#2), both scanned from
/// preserved CommandContainer EntryList fragments.
/// </summary>
public sealed class DimSubsetLessThanTypeRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R05-dim-subset-lt-type";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var (container, entryFragment, arrayType, _) in ArrayEntryChains.Resolve(context))
        {
            var entryDimensions = XmlFragmentInspector.FindDimensions(entryFragment.OuterXml);
            var typeDimensions = arrayType.Dimensions ?? [];
            if (entryDimensions.Count == 0 || entryDimensions.Count != typeDimensions.Count)
            {
                continue; // count mismatch is R02's finding
            }

            var location = $"{context.Path}/ContainerSet/{container.Name}";
            foreach (var issue in CompareBounds(location, "ArrayParameterRefEntry", arrayType.Name, entryDimensions, typeDimensions, candidateNumber: 6))
            {
                yield return issue;
            }
        }

        foreach (var issue in ValidateCommandContainerEntries(context))
        {
            yield return issue;
        }
    }

    /// <summary>Candidates #1/#2: array entries in preserved CommandContainer EntryLists.</summary>
    private IEnumerable<ValidationIssue> ValidateCommandContainerEntries(SpaceSystemContext context)
    {
        foreach (var metaCommand in context.Node.CommandMetaData?.MetaCommands ?? [])
        {
            var entryList = (metaCommand.CommandContainer?.Preserved ?? []).FirstOrDefault(f => f.ElementName == "EntryList");
            if (entryList is null)
            {
                continue;
            }
            var location = $"{context.Path}/CommandMetaData/MetaCommandSet/{metaCommand.Name}/CommandContainer";
            IReadOnlyList<ModeledArguments.Scoped>? visibleArguments = null;

            foreach (var (entryName, entryXml) in ArgumentScanner.ChildElements(entryList.OuterXml))
            {
                if (entryName == "ArrayArgumentRefEntry")
                {
                    if (XmlFragmentInspector.RootAttribute(entryXml, "argumentRef") is not { } argumentRef)
                    {
                        continue;
                    }
                    visibleArguments ??= ModeledArguments.Merged(context, metaCommand);
                    var argument = visibleArguments.FirstOrDefault(a => a.Decl.Name == argumentRef);
                    if (argument is null
                        || ModeledArguments.ResolveType(argument.Scope, argument.Decl.ArgumentTypeRef)
                            is not { Kind: ParameterTypeKind.Array } argumentArrayType)
                    {
                        continue;
                    }
                    foreach (var issue in CompareEntry(location, entryName, entryXml, argumentArrayType, candidateNumber: 1))
                    {
                        yield return issue;
                    }
                }
                else if (entryName == "ArrayParameterRefEntry")
                {
                    if (XmlFragmentInspector.RootAttribute(entryXml, "parameterRef") is not { } parameterRef)
                    {
                        continue;
                    }
                    var parameterResolution = NameReferenceResolver.Resolve(context, parameterRef, NamedItemKind.Parameter);
                    if (parameterResolution.Parameter is not { } parameter || parameterResolution.DefinedIn is not { } definedIn)
                    {
                        continue;
                    }
                    var typeResolution = NameReferenceResolver.Resolve(definedIn, parameter.ParameterTypeRef, NamedItemKind.ParameterType);
                    if (typeResolution.ParameterType is not { Kind: ParameterTypeKind.Array } parameterArrayType)
                    {
                        continue;
                    }
                    foreach (var issue in CompareEntry(location, entryName, entryXml, parameterArrayType, candidateNumber: 2))
                    {
                        yield return issue;
                    }
                }
            }
        }
    }

    private IEnumerable<ValidationIssue> CompareEntry(
        string location, string entryName, string entryXml, ParameterTypeDefinition arrayType, int candidateNumber)
    {
        var entryDimensions = XmlFragmentInspector.FindDimensions(entryXml);
        var typeDimensions = arrayType.Dimensions ?? [];
        return entryDimensions.Count == 0 || entryDimensions.Count != typeDimensions.Count
            ? []
            : CompareBounds(location, entryName, arrayType.Name, entryDimensions, typeDimensions, candidateNumber);
    }

    private IEnumerable<ValidationIssue> CompareBounds(
        string location,
        string entryName,
        string typeName,
        IReadOnlyList<XmlFragmentInspector.DimensionInfo> entryDimensions,
        IReadOnlyList<Dimension> typeDimensions,
        int candidateNumber)
    {
        var comparableCount = 0;
        var equalCount = 0;

        for (var i = 0; i < entryDimensions.Count; i++)
        {
            var entryEnd = entryDimensions[i].EndingFixed;
            var typeEnd = typeDimensions[i].EndingIndex.FixedValue;
            if (entryEnd is null || typeEnd is null)
            {
                continue;
            }

            comparableCount++;
            if (entryEnd > typeEnd)
            {
                yield return new ValidationIssue(RuleId, Severity, location,
                    $"{entryName} dimension {i} EndingIndex {entryEnd} exceeds type '{typeName}' bound {typeEnd}.",
                    CandidateNumber: candidateNumber);
            }
            else if (entryEnd == typeEnd)
            {
                equalCount++;
            }
        }

        if (comparableCount > 0 && comparableCount == entryDimensions.Count && equalCount == comparableCount)
        {
            yield return new ValidationIssue(RuleId, Severity, location,
                $"{entryName} DimensionList equals type '{typeName}' in every dimension — the same size is not a subset.",
                CandidateNumber: candidateNumber);
        }
    }
}

/// <summary>
/// XTCE-1.2-R06 (documented interpretation): "the order MUST ascend" (DimensionListType,
/// XSD line 3296) — within a Dimension, a fixed StartingIndex greater than its fixed
/// EndingIndex cannot ascend. Checked on modeled Array types and on raw
/// ArrayParameterRefEntry DimensionLists.
/// </summary>
public sealed class DimensionOrderMustAscendRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R06-dimensionlist-order-must-ascend";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var type in context.Node.TelemetryMetaData?.ParameterTypeSet ?? [])
        {
            if (type.Kind != ParameterTypeKind.Array)
            {
                continue;
            }
            for (var i = 0; i < (type.Dimensions?.Count ?? 0); i++)
            {
                var dimension = type.Dimensions![i];
                if (dimension.StartingIndex.FixedValue is { } start &&
                    dimension.EndingIndex.FixedValue is { } end && start > end)
                {
                    yield return new ValidationIssue(RuleId, Severity,
                        $"{context.Path}/ParameterTypeSet/{type.Name}",
                        $"Dimension {i} StartingIndex {start} is greater than EndingIndex {end} — dimension order must ascend.",
                        CandidateNumber: 61);
                }
            }
        }

        foreach (var container in context.Node.TelemetryMetaData?.ContainerSet ?? [])
        {
            foreach (var entry in container.EntryList)
            {
                if (entry.Kind != SequenceEntryKind.Raw || entry.RawXml?.ElementName != "ArrayParameterRefEntry")
                {
                    continue;
                }
                var dimensions = XmlFragmentInspector.FindDimensions(entry.RawXml.OuterXml);
                for (var i = 0; i < dimensions.Count; i++)
                {
                    if (dimensions[i].StartingFixed is { } start && dimensions[i].EndingFixed is { } end && start > end)
                    {
                        yield return new ValidationIssue(RuleId, Severity,
                            $"{context.Path}/ContainerSet/{container.Name}",
                            $"ArrayParameterRefEntry dimension {i} StartingIndex {start} is greater than EndingIndex {end} — dimension order must ascend.",
                            CandidateNumber: 61);
                    }
                }
            }
        }
    }
}

/// <summary>
/// Shared plumbing: every raw ArrayParameterRefEntry in the context's containers whose
/// parameterRef → Parameter → parameterTypeRef chain resolves to a MODELED Array type.
/// </summary>
internal static class ArrayEntryChains
{
    public static IEnumerable<(SequenceContainer Container, RawXmlFragment Entry, ParameterTypeDefinition ArrayType, SpaceSystemContext DefinedIn)>
        Resolve(SpaceSystemContext context)
    {
        foreach (var container in context.Node.TelemetryMetaData?.ContainerSet ?? [])
        {
            foreach (var entry in container.EntryList)
            {
                if (entry.Kind != SequenceEntryKind.Raw || entry.RawXml?.ElementName != "ArrayParameterRefEntry")
                {
                    continue;
                }
                if (XmlFragmentInspector.RootAttribute(entry.RawXml.OuterXml, "parameterRef") is not { } parameterRef)
                {
                    continue;
                }

                var parameterResolution = NameReferenceResolver.Resolve(context, parameterRef, NamedItemKind.Parameter);
                if (parameterResolution.Parameter is not { } parameter || parameterResolution.DefinedIn is not { } definedIn)
                {
                    continue;
                }

                var typeResolution = NameReferenceResolver.Resolve(definedIn, parameter.ParameterTypeRef, NamedItemKind.ParameterType);
                if (typeResolution.ParameterType is { Kind: ParameterTypeKind.Array } arrayType)
                {
                    yield return (container, entry.RawXml, arrayType, definedIn);
                }
            }
        }
    }
}
