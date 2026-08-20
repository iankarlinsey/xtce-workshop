using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R01: usage of calendar-variable time units (days/months/years) should be
/// flagged (TimeUnitsType, XSD line 5273 — their length varies with leap rules, making
/// them ambiguous as encoding units). PARTIAL: TimeUnitsType appears at two sites; the
/// Encoding/units attribute on time types is checked by peeking at the preserved Encoding
/// fragment, while TimeAlarmRangesType/timeUnits sits deep inside preserved alarm content
/// and is unreachable by construction.
/// </summary>
public sealed class AmbiguousTimeUnitsRule : IValidationRule
{
    private static readonly string[] AmbiguousUnits = ["days", "months", "years"];

    public string RuleId => "XTCE-1.2-R01-ambiguous-time-units-flagged";
    public ValidationSeverity Severity => ValidationSeverity.Warning;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var type in context.Node.TelemetryMetaData?.ParameterTypeSet ?? [])
        {
            if (type.Kind is not (ParameterTypeKind.RelativeTime or ParameterTypeKind.AbsoluteTime))
            {
                continue;
            }

            foreach (var fragment in type.Preserved ?? [])
            {
                if (fragment.ElementName != "Encoding")
                {
                    continue;
                }

                var units = XmlFragmentInspector.RootAttribute(fragment.OuterXml, "units");
                if (units is not null && AmbiguousUnits.Contains(units))
                {
                    yield return new ValidationIssue(
                        RuleId,
                        Severity,
                        $"{context.Path}/ParameterTypeSet/{type.Name}",
                        $"Encoding units '{units}' is calendar-variable and ambiguous — prefer seconds or picoSeconds.",
                        CandidateNumber: 106);
                }
            }
        }
    }
}
