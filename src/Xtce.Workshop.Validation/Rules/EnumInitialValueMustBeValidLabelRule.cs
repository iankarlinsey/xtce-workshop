using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R07: an enumerated type's own initialValue, when set, must be a label that
/// actually appears in its EnumerationList. Both matrix citations are checked:
/// EnumeratedDataType/initialValue on modeled parameter types (candidate #63), and
/// ArgumentEnumeratedDataType/initialValue on EnumeratedArgumentTypes scanned out of the
/// preserved ArgumentTypeSet (candidate #62, issue #49).
/// </summary>
public sealed class EnumInitialValueMustBeValidLabelRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R07-enum-initial-value-must-be-valid-label";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var argumentType in ArgumentScanner.ScanArgumentTypes(context.Node.CommandMetaData))
        {
            if (argumentType.Kind != ParameterTypeKind.Enumerated || argumentType.InitialValue is null)
            {
                continue;
            }
            if (!(argumentType.Enumerations ?? []).Any(e => e.Label == argumentType.InitialValue))
            {
                yield return new ValidationIssue(
                    RuleId,
                    Severity,
                    $"{context.Path}/CommandMetaData/ArgumentTypeSet/{argumentType.Name}",
                    $"initialValue '{argumentType.InitialValue}' is not a valid label in {argumentType.Name}'s EnumerationList.",
                    CandidateNumber: 62);
            }
        }

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
