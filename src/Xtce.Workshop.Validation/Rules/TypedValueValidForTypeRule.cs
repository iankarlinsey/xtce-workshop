using System.Globalization;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R15: a value literal must be valid for, and within range of, its declared or
/// referenced data type. research/xtce-1.2-rule-matrix.csv cites 7 owner locations; this
/// rule implements exactly one — ParameterType/initialValue (a Parameter's own initialValue,
/// which overrides its referenced ParameterTypeDefinition's initialValue). The other 6
/// (ArgumentAssignmentType/argumentValue, ArgumentComparisonType/value,
/// ArgumentType/initialValue, ParameterToSetType/NewValue, ComparisonCheckType/Value,
/// ComparisonType/value) need MetaCommand/Container modeling this project doesn't have yet —
/// see summary.md's Architecture Decisions. This rule is intentionally partial, not "done."
///
/// Only parameterTypeRef values that resolve to a type in the SAME SpaceSystem's own
/// ParameterTypeSet are checked. XTCE name references may be relative/absolute paths across
/// the whole SpaceSystem tree; resolving those, and flagging references that don't resolve
/// at all, is rule R11 (no-dangling-name-references), not yet implemented. A parameterTypeRef
/// this rule can't resolve locally is silently skipped rather than flagged, to avoid false
/// positives on legitimate cross-subsystem references.
/// </summary>
public sealed class TypedValueValidForTypeRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R15-typed-value-valid-for-type";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> ValidateSpaceSystem(SpaceSystem spaceSystem, string path)
    {
        if (spaceSystem.TelemetryMetaData is null)
        {
            yield break;
        }

        var typesByName = spaceSystem.TelemetryMetaData.ParameterTypeSet.ToDictionary(t => t.Name);

        foreach (var parameter in spaceSystem.TelemetryMetaData.ParameterSet)
        {
            if (parameter.InitialValue is null || !typesByName.TryGetValue(parameter.ParameterTypeRef, out var type))
            {
                continue;
            }

            var error = Describe(type, parameter.InitialValue);
            if (error is not null)
            {
                yield return new ValidationIssue(RuleId, Severity, $"{path}/ParameterSet/{parameter.Name}", error);
            }
        }
    }

    private static string? Describe(ParameterTypeDefinition type, string value) => type.Kind switch
    {
        ParameterTypeKind.Integer => DescribeInteger(type, value),
        ParameterTypeKind.Float => DescribeFloat(value),
        ParameterTypeKind.String => null,
        ParameterTypeKind.Boolean => DescribeBoolean(type, value),
        ParameterTypeKind.Enumerated => DescribeEnumerated(type, value),
        _ => null,
    };

    private static string? DescribeInteger(ParameterTypeDefinition type, string value)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
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
