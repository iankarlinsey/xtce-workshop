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
/// parameterTypeRef resolution goes through NameReferenceResolver (issue #25), so a
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
                yield return new ValidationIssue(RuleId, Severity, $"{context.Path}/ParameterSet/{parameter.Name}", error);
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
