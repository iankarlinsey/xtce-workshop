using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R16: inheritance chains must not form loops — "baseType NameReferences that
/// form loops are illegal" (CCSDS 660.1-G-2 4.3.2.2.3.1(d)), "BaseContainers that form
/// loops are illegal" (4.3.4.9.5.2(d)), "BaseMetaCommands that form loops are illegal"
/// (4.4.5.2.3.4(b)); the XSD's own baseType appinfo says "No circular derivations".
/// A chain that ENTERS a loop is flagged at its starting item — each member of a cycle
/// therefore gets its own finding, deterministically. Opaque (preserved-only) links end
/// the walk.
/// </summary>
public sealed class NoInheritanceCyclesRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R16-no-inheritance-cycles";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var type in context.Node.TelemetryMetaData?.ParameterTypeSet ?? [])
        {
            if (ChainEntersLoop(type, t => ResolveBaseType(context, t)))
            {
                yield return new ValidationIssue(RuleId, Severity,
                    $"{context.Path}/ParameterTypeSet/{type.Name}",
                    $"Parameter type '{type.Name}' has a baseType inheritance chain that forms a loop.");
            }
        }

        foreach (var container in context.Node.TelemetryMetaData?.ContainerSet ?? [])
        {
            if (ChainEntersLoop(container, c => c.BaseContainer is { } baseContainer
                    ? NameReferenceResolver.Resolve(context, baseContainer.ContainerRef, NamedItemKind.Container).Container
                    : null))
            {
                yield return new ValidationIssue(RuleId, Severity,
                    $"{context.Path}/ContainerSet/{container.Name}",
                    $"Container '{container.Name}' has a BaseContainer inheritance chain that forms a loop.");
            }
        }

        foreach (var metaCommand in context.Node.CommandMetaData?.MetaCommands ?? [])
        {
            if (ChainEntersLoop(metaCommand, m => m.BaseMetaCommandRef is { } baseRef
                    ? NameReferenceResolver.Resolve(context, baseRef, NamedItemKind.MetaCommand).MetaCommand
                    : null))
            {
                yield return new ValidationIssue(RuleId, Severity,
                    $"{context.Path}/CommandMetaData/MetaCommandSet/{metaCommand.Name}",
                    $"MetaCommand '{metaCommand.Name}' has a BaseMetaCommand inheritance chain that forms a loop.");
            }
        }
    }

    internal static ParameterTypeDefinition? ResolveBaseType(SpaceSystemContext context, ParameterTypeDefinition type)
    {
        var baseTypeRef = (type.PreservedAttributes ?? []).FirstOrDefault(a => a.Name == "baseType")?.Value;
        return baseTypeRef is null
            ? null
            : NameReferenceResolver.Resolve(context, baseTypeRef, NamedItemKind.ParameterType).ParameterType;
    }

    private static bool ChainEntersLoop<T>(T start, Func<T, T?> next) where T : class
    {
        var visited = new HashSet<T>(ReferenceEqualityComparer.Instance) { start };
        var current = next(start);
        while (current is not null)
        {
            if (!visited.Add(current))
            {
                return true;
            }
            current = next(current);
        }
        return false;
    }
}

/// <summary>
/// XTCE-1.2-R17: a string encoding must not specify conflicting length-determination
/// methods (CCSDS 660.1-G-2 4.3.2.2.5.5 RECOMMENDATIONs — "It is an error if SizeInBits is
/// set and both TerminationChar and LeadingSize are set"; for Variable, where the dynamic
/// lookup is schema-required, "it [is] an error for more than one of these to be set, even
/// though the syntax allows it"). StringDataEncoding lives in preserved fragments.
/// </summary>
public sealed class StringLengthSpecConflictsRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R17-string-length-spec-conflicts";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var (fragment, location) in FragmentEnumerator.EnumerateNode(context))
        {
            foreach (var encoding in XmlFragmentInspector.FindStringEncodings(fragment.OuterXml))
            {
                if (encoding.IsVariable && (encoding.HasTerminationChar || encoding.HasLeadingSize))
                {
                    yield return new ValidationIssue(RuleId, Severity, location,
                        "Variable string encoding also sets LeadingSize/TerminationChar — more than one length-determination method is an error.");
                }
                else if (!encoding.IsVariable && encoding.HasTerminationChar && encoding.HasLeadingSize)
                {
                    yield return new ValidationIssue(RuleId, Severity, location,
                        "String encoding sets SizeInBits with BOTH TerminationChar and LeadingSize — an error per CCSDS 660.1-G-2.");
                }
            }
        }
    }
}

/// <summary>
/// XTCE-1.2-R18 (PARTIAL — modeled parents only): a derived parameter type may not
/// override non-overridable parent items (CCSDS 660.1-G-2 Table 4-2:
/// StringParameterType/@characterWidth "may not override parent content";
/// IntegerParameterType/@sizeInBits and @signed "cannot override the parent, including
/// default values"), and per the XSD's own appinfo a type "must be derived from a like
/// type (e.g., String from String)".
/// </summary>
public sealed class TypeInheritanceOverrideRestrictionsRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R18-type-inheritance-override-restrictions";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var type in context.Node.TelemetryMetaData?.ParameterTypeSet ?? [])
        {
            var parent = NoInheritanceCyclesRule.ResolveBaseType(context, type);
            if (parent is null)
            {
                continue;
            }
            var location = $"{context.Path}/ParameterTypeSet/{type.Name}";

            if (parent.Kind != type.Kind)
            {
                yield return new ValidationIssue(RuleId, Severity, location,
                    $"'{type.Name}' ({type.Kind}) derives from '{parent.Name}' ({parent.Kind}) — a type must be derived from a like type.");
                continue;
            }

            if (type.Kind == ParameterTypeKind.Integer)
            {
                if (type.SizeInBits is { } childSize && childSize != (parent.SizeInBits ?? 32))
                {
                    yield return new ValidationIssue(RuleId, Severity, location,
                        $"'{type.Name}' overrides sizeInBits ({childSize} vs parent's effective {parent.SizeInBits ?? 32}) — the child cannot override the parent, including default values.");
                }
                if (type.Signed is { } childSigned && childSigned != (parent.Signed ?? true))
                {
                    yield return new ValidationIssue(RuleId, Severity, location,
                        $"'{type.Name}' overrides signed ({childSigned} vs parent's effective {parent.Signed ?? true}) — the child cannot override the parent.");
                }
            }

            if (type.Kind == ParameterTypeKind.String)
            {
                var childWidth = (type.PreservedAttributes ?? []).FirstOrDefault(a => a.Name == "characterWidth")?.Value;
                var parentWidth = (parent.PreservedAttributes ?? []).FirstOrDefault(a => a.Name == "characterWidth")?.Value;
                if (childWidth is not null && parentWidth is not null && childWidth != parentWidth)
                {
                    yield return new ValidationIssue(RuleId, Severity, location,
                        $"'{type.Name}' overrides characterWidth ('{childWidth}' vs parent's '{parentWidth}') — the child may not override parent content.");
                }
            }
        }
    }
}

/// <summary>
/// XTCE-1.2-R19 (PARTIAL — explicit attributes only): "spanOfInterestInSeconds ... must be
/// set to a positive value if changeType is set to 'changePerSecond'" (CCSDS 660.1-G-2
/// 4.3.2.3.7.3.5). Anchored on explicitly present attributes to stay false-positive-free:
/// which elements would receive the schema defaults can't be known from a fragment alone.
/// Spec quirk worth knowing: the schema's own defaults (changeType=changePerSecond,
/// spanOfInterestInSeconds=0) violate this rule as written — an all-defaults change alarm
/// is undetectable here and arguably invalid by the book's text.
/// </summary>
public sealed class ChangePerSecondRequiresPositiveSpanRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R19-changepersecond-requires-positive-span";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var (fragment, location) in FragmentEnumerator.EnumerateNode(context))
        {
            foreach (var alarm in XmlFragmentInspector.FindChangeAlarmAttributes(fragment.OuterXml))
            {
                var explicitPerSecond = alarm.ChangeType == "changePerSecond";
                var defaultedPerSecondWithBadSpan = alarm.ChangeType is null && alarm.SpanOfInterestInSeconds <= 0;
                var spanNotPositive = (alarm.SpanOfInterestInSeconds ?? 0) <= 0;

                if ((explicitPerSecond && spanNotPositive) || defaultedPerSecondWithBadSpan)
                {
                    yield return new ValidationIssue(RuleId, Severity, location,
                        "changeType 'changePerSecond' (explicit or defaulted) requires a positive spanOfInterestInSeconds.");
                }
            }
        }
    }
}

/// <summary>
/// XTCE-1.2-R20 (PARTIAL — explicit dataSource only; warning): "If the DataEncoding is not
/// set and the dataSource is set to telemetered (either explicitly or implied), the
/// implementation should at least issue a warning" (CCSDS 660.1-G-2, which itself
/// prescribes warning severity). The "implied" case (absent dataSource defaults to
/// telemetered) is deliberately not covered — it would flag every minimal document; the
/// gap is recorded in the matrix. Time kinds are R14's business (missing Encoding there is
/// an error regardless); Array/Aggregate get their encodings via element/member types.
/// </summary>
public sealed class TelemeteredParameterRequiresEncodingRule : IValidationRule
{
    private static readonly string[] EncodingElements =
        ["BinaryDataEncoding", "FloatDataEncoding", "IntegerDataEncoding", "StringDataEncoding"];

    public string RuleId => "XTCE-1.2-R20-telemetered-parameter-requires-encoding";
    public ValidationSeverity Severity => ValidationSeverity.Warning;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var parameter in context.Node.TelemetryMetaData?.ParameterSet ?? [])
        {
            var properties = (parameter.Preserved ?? []).FirstOrDefault(f => f.ElementName == "ParameterProperties");
            if (properties is null ||
                XmlFragmentInspector.RootAttribute(properties.OuterXml, "dataSource") != "telemetered")
            {
                continue;
            }

            var resolution = NameReferenceResolver.Resolve(context, parameter.ParameterTypeRef, NamedItemKind.ParameterType);
            if (resolution.ParameterType is not { } type)
            {
                continue;
            }
            if (type.Kind is ParameterTypeKind.RelativeTime or ParameterTypeKind.AbsoluteTime
                or ParameterTypeKind.Array or ParameterTypeKind.Aggregate)
            {
                continue;
            }

            var hasEncoding = (type.Preserved ?? []).Any(f => EncodingElements.Contains(f.ElementName));
            if (!hasEncoding)
            {
                yield return new ValidationIssue(RuleId, Severity,
                    $"{context.Path}/ParameterSet/{parameter.Name}",
                    $"Parameter '{parameter.Name}' is explicitly telemetered but its type '{type.Name}' has no data encoding.");
            }
        }
    }
}
