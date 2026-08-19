using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R14: a time data type must always have a data encoding set, even when not
/// telemetered — the explicit exception to other primitives (BaseTimeDataType, XSD line
/// 3166: "if the time data type is not telemetered, it still must have a data encoding
/// set"). The Encoding element isn't modeled — it lives in the type's Preserved fragments —
/// so the check is fragment-presence. A type carrying a baseType attribute (in
/// PreservedAttributes) may inherit its encoding from the base; resolving time-type
/// inheritance chains is out of scope, so baseType presence skips the check rather than
/// risking a false positive.
/// </summary>
public sealed class TimeDataTypeRequiresEncodingRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R14-time-datatype-requires-encoding";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var type in context.Node.TelemetryMetaData?.ParameterTypeSet ?? [])
        {
            if (type.Kind is not (ParameterTypeKind.RelativeTime or ParameterTypeKind.AbsoluteTime))
            {
                continue;
            }

            var hasEncoding = (type.Preserved ?? []).Any(f => f.ElementName == "Encoding");
            var hasBaseType = (type.PreservedAttributes ?? []).Any(a => a.Name == "baseType");

            if (!hasEncoding && !hasBaseType)
            {
                yield return new ValidationIssue(
                    RuleId,
                    Severity,
                    $"{context.Path}/ParameterTypeSet/{type.Name}",
                    $"{type.Name} is a time data type and must have an Encoding element, even when not telemetered.");
            }
        }
    }
}
