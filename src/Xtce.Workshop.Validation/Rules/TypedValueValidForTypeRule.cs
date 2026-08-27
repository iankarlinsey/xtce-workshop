using System.Globalization;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R15: a value literal must be valid for, and within range of, its declared or
/// referenced data type, at every citation site listed for R15 in
/// research/xtce-1.2-rule-matrix.csv: parameter and aggregate-member initial values,
/// restriction and verifier comparison values, argument initial values and assignments,
/// and ParameterToSet new values.
///
/// parameterTypeRef resolution goes through NameReferenceResolver, so a
/// Parameter whose type lives in an ancestor or another SpaceSystem is checked too. A ref
/// that resolves to an unmodeled (opaque, preserved) type is skipped — its constraints
/// can't be inspected; a ref that doesn't resolve at all is R11's finding, not R15's.
/// </summary>
public sealed class TypedValueValidForTypeRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R15-typed-value-valid-for-type";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var issue in ValidateCommandSide(context))
        {
            yield return issue;
        }

        if (context.Node.TelemetryMetaData is null)
        {
            yield break;
        }

        foreach (var parameter in context.Node.TelemetryMetaData.ParameterSet)
        {
            if (parameter.InitialValue is null)
            {
                continue;
            }

            var resolution = NameReferenceResolver.Resolve(context, parameter.ParameterTypeRef, NamedItemKind.ParameterType);
            if (resolution.ParameterType is not { } type)
            {
                continue;
            }

            var error = Describe(type, parameter.InitialValue);
            if (error is not null)
            {
                yield return new ValidationIssue(RuleId, Severity, $"{context.Path}/ParameterSet/{parameter.Name}", error,
                    CandidateNumber: 29);
            }
        }

        // Aggregate member initialValue overrides — same must-match-the-type semantics.
        foreach (var type in context.Node.TelemetryMetaData.ParameterTypeSet)
        {
            foreach (var member in type.Members ?? [])
            {
                if (member.InitialValue is null)
                {
                    continue;
                }
                var memberTypeResolution = NameReferenceResolver.Resolve(context, member.TypeRef, NamedItemKind.ParameterType);
                if (memberTypeResolution.ParameterType is not { } memberType)
                {
                    continue;
                }
                var error = Describe(memberType, member.InitialValue);
                if (error is not null)
                {
                    yield return new ValidationIssue(RuleId, Severity,
                        $"{context.Path}/ParameterTypeSet/{type.Name}/{member.Name}", error);
                }
            }
        }

        // ComparisonType/value (a listed matrix citation): a RestrictionCriteria
        // Comparison's value literal against the compared parameter's resolved type.
        foreach (var container in context.Node.TelemetryMetaData.ContainerSet ?? [])
        {
            var comparisons = new List<Comparison>();
            if (container.BaseContainer?.RestrictionCriteria is { } criteria)
            {
                if (criteria.Comparison is { } single)
                {
                    comparisons.Add(single);
                }
                comparisons.AddRange(criteria.ComparisonList ?? []);
            }

            foreach (var comparison in comparisons)
            {
                var parameterResolution = NameReferenceResolver.Resolve(context, comparison.ParameterRef, NamedItemKind.Parameter);
                if (parameterResolution.Parameter is not { } parameter || parameterResolution.DefinedIn is not { } definedIn)
                {
                    continue;
                }
                var typeResolution = NameReferenceResolver.Resolve(definedIn, parameter.ParameterTypeRef, NamedItemKind.ParameterType);
                if (typeResolution.ParameterType is not { } comparedType)
                {
                    continue;
                }
                var error = Describe(comparedType, comparison.Value);
                if (error is not null)
                {
                    yield return new ValidationIssue(RuleId, Severity,
                        $"{context.Path}/ContainerSet/{container.Name}",
                        $"Comparison against '{comparison.ParameterRef}': {error}",
                        CandidateNumber: 88);
                }
            }

            // ComparisonCheckType/Value (candidate #85): a RestrictionCriteria
            // BooleanExpression rides as a preserved fragment; its Conditions compare a
            // ParameterInstanceRef against a Value literal.
            if (container.BaseContainer?.RestrictionCriteria?.Raw is { } rawCriteria)
            {
                foreach (var condition in ArgumentScanner.ScanComparisons(rawCriteria.OuterXml))
                {
                    if (condition.Form != ArgumentScanner.ComparisonForm.ConditionValue || condition.ParameterRef is null)
                    {
                        continue;
                    }
                    var error = DescribeAgainstParameter(context, condition.ParameterRef, condition.Value);
                    if (error is not null)
                    {
                        yield return new ValidationIssue(RuleId, Severity,
                            $"{context.Path}/ContainerSet/{container.Name}",
                            $"Condition against '{condition.ParameterRef}': {error}",
                            CandidateNumber: 85);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Command-side value literals: argument initial values (#39), base-command
    /// argument assignments (#33), ParameterToSet new values (#45), and the comparison
    /// forms inside constraints/verifiers — ArgumentComparisonType values (#34),
    /// ArgumentComparisonCheckType Condition values (#35), and plain ComparisonType values
    /// riding in preserved command fragments (#88's site). Everything is evaluated from
    /// preserved fragments via ArgumentScanner; argument types resolve unqualified-only.
    /// </summary>
    private IEnumerable<ValidationIssue> ValidateCommandSide(SpaceSystemContext context)
    {
        if (context.Node.CommandMetaData is not { } commandMetaData)
        {
            yield break;
        }

        foreach (var metaCommand in commandMetaData.MetaCommands)
        {
            var location = $"{context.Path}/CommandMetaData/MetaCommandSet/{metaCommand.Name}";
            var visibleArguments = ModeledArguments.Merged(context, metaCommand);

            // #39 — an Argument's initialValue must fit its argumentTypeRef'd type.
            foreach (var declaration in metaCommand.Arguments ?? [])
            {
                if (declaration.InitialValue is null)
                {
                    continue;
                }
                if (ModeledArguments.ResolveType(context, declaration.ArgumentTypeRef) is not { } argumentType)
                {
                    continue;
                }
                var error = Describe(argumentType, declaration.InitialValue);
                if (error is not null)
                {
                    yield return new ValidationIssue(RuleId, Severity, location,
                        $"Argument '{declaration.Name}': {error}", CandidateNumber: 39);
                }
            }

            // #33 — ArgumentAssignment values must fit the (inherited) argument's type.
            foreach (var assignment in metaCommand.ArgumentAssignments ?? [])
            {
                var target = visibleArguments.FirstOrDefault(a => a.Decl.Name == assignment.ArgumentName);
                if (target is null
                    || ModeledArguments.ResolveType(target.Scope, target.Decl.ArgumentTypeRef) is not { } assignedType)
                {
                    continue;
                }
                var error = Describe(assignedType, assignment.ArgumentValue);
                if (error is not null)
                {
                    yield return new ValidationIssue(RuleId, Severity, location,
                        $"ArgumentAssignment '{assignment.ArgumentName}': {error}", CandidateNumber: 33);
                }
            }

            // #45 — ParameterToSet NewValue must fit the target parameter's type
            // (Derivation-based sets carry no literal and are skipped).
            foreach (var parameterToSet in ArgumentScanner.ScanParameterToSets(metaCommand))
            {
                if (parameterToSet.NewValue is null)
                {
                    continue;
                }
                var error = DescribeAgainstParameter(context, parameterToSet.ParameterRef, parameterToSet.NewValue);
                if (error is not null)
                {
                    yield return new ValidationIssue(RuleId, Severity, location,
                        $"ParameterToSet '{parameterToSet.ParameterRef}': {error}", CandidateNumber: 45);
                }
            }

            // #34 / #35 / #88 — comparison literals anywhere in this command's fragments.
            foreach (var fragment in ArgumentScanner.CommandFragments(metaCommand))
            {
                foreach (var comparison in ArgumentScanner.ScanComparisons(fragment.OuterXml))
                {
                    string? error;
                    string subject;
                    if (comparison.ArgumentRef is { } argumentRef)
                    {
                        var target = visibleArguments.FirstOrDefault(a => a.Decl.Name == argumentRef);
                        if (target is null
                            || ModeledArguments.ResolveType(target.Scope, target.Decl.ArgumentTypeRef) is not { } argumentType)
                        {
                            continue;
                        }
                        error = Describe(argumentType, comparison.Value);
                        subject = $"argument '{argumentRef}'";
                    }
                    else if (comparison.ParameterRef is { } parameterRef)
                    {
                        error = DescribeAgainstParameter(context, parameterRef, comparison.Value);
                        subject = $"'{parameterRef}'";
                    }
                    else
                    {
                        continue;
                    }

                    if (error is not null)
                    {
                        var candidate = comparison.Form switch
                        {
                            ArgumentScanner.ComparisonForm.ConditionValue => 35,
                            ArgumentScanner.ComparisonForm.InstanceRef => 34,
                            _ => 88,
                        };
                        yield return new ValidationIssue(RuleId, Severity, location,
                            $"Comparison against {subject}: {error}", CandidateNumber: candidate);
                    }
                }
            }
        }
    }

    /// <summary>Runs the typed-value check against a parameterRef's resolved modeled type; null if unresolvable/opaque.</summary>
    private static string? DescribeAgainstParameter(SpaceSystemContext context, string parameterRef, string value)
    {
        var parameterResolution = NameReferenceResolver.Resolve(context, parameterRef, NamedItemKind.Parameter);
        if (parameterResolution.Parameter is not { } parameter || parameterResolution.DefinedIn is not { } definedIn)
        {
            return null;
        }
        var typeResolution = NameReferenceResolver.Resolve(definedIn, parameter.ParameterTypeRef, NamedItemKind.ParameterType);
        return typeResolution.ParameterType is { } parameterType ? Describe(parameterType, value) : null;
    }

    private static string? Describe(ParameterTypeDefinition type, string value) => type.Kind switch
    {
        ParameterTypeKind.Integer => DescribeInteger(type, value),
        ParameterTypeKind.Float => DescribeFloat(value),
        ParameterTypeKind.String => null,
        ParameterTypeKind.Boolean => DescribeBoolean(type, value),
        ParameterTypeKind.Enumerated => DescribeEnumerated(type, value),
        ParameterTypeKind.Binary => DescribeBinary(value),
        ParameterTypeKind.RelativeTime => DescribeRelativeTime(value),
        ParameterTypeKind.AbsoluteTime => DescribeAbsoluteTime(value),
        _ => null,
    };

    // xs:hexBinary: hex digits, even count (each byte is two digits).
    private static string? DescribeBinary(string value) =>
        value.Length % 2 == 0 && value.All(Uri.IsHexDigit)
            ? null
            : $"initialValue '{value}' is not valid hexBinary (even number of hex digits) for its Binary type.";

    private static string? DescribeRelativeTime(string value)
    {
        try
        {
            System.Xml.XmlConvert.ToTimeSpan(value);
            return null;
        }
        catch (FormatException)
        {
            return $"initialValue '{value}' is not a valid xs:duration for its RelativeTime type.";
        }
    }

    private static string? DescribeAbsoluteTime(string value)
    {
        try
        {
            System.Xml.XmlConvert.ToDateTimeOffset(value);
            return null;
        }
        catch (FormatException)
        {
            return $"initialValue '{value}' is not a valid xs:dateTime for its AbsoluteTime type.";
        }
    }

    /// <summary>
    /// Parses an XTCE integer literal: base 10 by default, or 0b/0B (binary), 0o/0O
    /// (octal), 0x/0X (hex) prefixed — IntegerDataType/@initialValue's own documentation
    /// spells these out, and ComparisonType values use the same convention.
    /// </summary>
    internal static bool TryParseXtceInteger(string value, out long parsed)
    {
        parsed = 0;
        var negative = value.StartsWith('-');
        var digits = negative ? value[1..] : value;

        try
        {
            if (digits.Length > 2 && digits[0] == '0')
            {
                var radix = char.ToLowerInvariant(digits[1]) switch
                {
                    'x' => 16,
                    'o' => 8,
                    'b' => 2,
                    _ => 0,
                };
                if (radix != 0)
                {
                    parsed = Convert.ToInt64(digits[2..], radix);
                    if (negative)
                    {
                        parsed = -parsed;
                    }
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            return false;
        }

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }

    private static string? DescribeInteger(ParameterTypeDefinition type, string value)
    {
        if (!TryParseXtceInteger(value, out var parsed))
        {
            return $"initialValue '{value}' is not a valid integer for its Integer type.";
        }

        var sizeInBits = type.SizeInBits ?? 32;
        var signed = type.Signed ?? true;

        // sizeInBits outside 1..63 can't be range-checked against a 64-bit `long` (and
        // unsigned 64-bit range-checking is a known gap regardless — a value above
        // long.MaxValue would already have failed the parse above). Parse-validity alone is
        // still a meaningful check even when the range itself can't be.
        if (sizeInBits is <= 0 or > 63)
        {
            return null;
        }

        var min = signed ? -(1L << (int)(sizeInBits - 1)) : 0L;
        var max = signed ? (1L << (int)(sizeInBits - 1)) - 1 : (1L << (int)sizeInBits) - 1;

        return parsed < min || parsed > max
            ? $"initialValue '{value}' is out of range for a {(signed ? "signed" : "unsigned")} {sizeInBits}-bit Integer type ([{min}, {max}])."
            : null;
    }

    private static string? DescribeFloat(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            ? null
            : $"initialValue '{value}' is not a valid floating-point number for its Float type.";

    private static string? DescribeBoolean(ParameterTypeDefinition type, string value)
    {
        var oneStringValue = type.OneStringValue ?? "True";
        var zeroStringValue = type.ZeroStringValue ?? "False";
        return value == oneStringValue || value == zeroStringValue
            ? null
            : $"initialValue '{value}' matches neither oneStringValue ('{oneStringValue}') nor zeroStringValue ('{zeroStringValue}') for its Boolean type.";
    }

    private static string? DescribeEnumerated(ParameterTypeDefinition type, string value) =>
        (type.Enumerations ?? []).Any(e => e.Label == value)
            ? null
            : $"initialValue '{value}' is not a valid label in its referenced type's EnumerationList.";
}
