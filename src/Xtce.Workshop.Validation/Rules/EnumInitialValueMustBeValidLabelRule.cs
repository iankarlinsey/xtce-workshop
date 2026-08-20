using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R07: an EnumeratedParameterType's own initialValue, when set, must be a label
/// that actually appears in its EnumerationList. See
/// research/xtce-1.2-rule-matrix.csv (citations: ArgumentEnumeratedDataType/initialValue,
/// EnumeratedDataType/initialValue). Only the EnumeratedDataType/initialValue citation is
/// checked here — the Argument-side citation needs MetaCommand/Argument modeling this
/// project doesn't have yet.
/// </summary>
public sealed class EnumInitialValueMustBeValidLabelRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R07-enum-initial-value-must-be-valid-label";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        if (context.Node.TelemetryMetaData is null)
        {
            yield break;
        }

        foreach (var type in context.Node.TelemetryMetaData.ParameterTypeSet)
        {
            if (type.Kind != ParameterTypeKind.Enumerated || type.InitialValue is null)
            {
                continue;
            }

            var labels = type.Enumerations ?? [];
            if (!labels.Any(e => e.Label == type.InitialValue))
            {
                yield return new ValidationIssue(
                    RuleId,
                    Severity,
                    $"{context.Path}/ParameterTypeSet/{type.Name}",
                    $"initialValue '{type.InitialValue}' is not a valid label in {type.Name}'s EnumerationList.",
                    CandidateNumber: 63);
            }
        }
    }
}
