using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R22 (warning): a FixedValueEntry's binaryValue "should have sufficient bit
/// length to accomodate the size in bits" (ArgumentFixedValueEntryType/binaryValue, XSD
/// line 309 — candidate #3, recovered by the blind re-triage in issue #48 after being
/// misdismissed in Phase B). Oversize is legal — the doc defines most-significant-bit
/// truncation — so only insufficiency is flagged. FixedValueEntry appears in command
/// container entry lists, which ride as preserved fragments; checked by document-wide
/// fragment inspection.
/// </summary>
public sealed class FixedValueBitLengthRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R22-fixedvalue-bitlength-sufficient";
    public ValidationSeverity Severity => ValidationSeverity.Warning;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var (fragment, location) in FragmentEnumerator.EnumerateNode(context))
        {
            foreach (var entry in XmlFragmentInspector.FindFixedValueEntries(fragment.OuterXml))
            {
                if (entry.BinaryValue is null || entry.SizeInBits is null)
                {
                    continue;
                }
                var availableBits = (long)entry.BinaryValue.Length * 4; // hexBinary: 4 bits per digit
                if (availableBits < entry.SizeInBits)
                {
                    yield return new ValidationIssue(RuleId, Severity, location,
                        $"FixedValueEntry binaryValue '{entry.BinaryValue}' provides {availableBits} bits but sizeInBits is {entry.SizeInBits} — the value should have sufficient bit length.",
                        CandidateNumber: 3);
                }
            }
        }
    }
}

/// <summary>
/// XTCE-1.2-R23 (warning): "For a constant data source, then 'readOnly' should be 'true'"
/// (ParameterPropertiesType/readOnly, XSD line 1040 — candidate #27, recovered by the
/// blind re-triage in issue #48). Only an EXPLICIT readOnly="false" alongside
/// dataSource="constant" is flagged — when readOnly is absent, the doc itself says
/// "application implementations may choose to implicitly enforce this."
/// </summary>
public sealed class ConstantDataSourceReadOnlyRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R23-constant-datasource-should-be-readonly";
    public ValidationSeverity Severity => ValidationSeverity.Warning;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var parameter in context.Node.TelemetryMetaData?.ParameterSet ?? [])
        {
            var properties = (parameter.Preserved ?? []).FirstOrDefault(f => f.ElementName == "ParameterProperties");
            if (properties is null)
            {
                continue;
            }
            var dataSource = XmlFragmentInspector.RootAttribute(properties.OuterXml, "dataSource");
            var readOnly = XmlFragmentInspector.RootAttribute(properties.OuterXml, "readOnly");
            if (dataSource == "constant" && readOnly == "false")
            {
                yield return new ValidationIssue(RuleId, Severity,
                    $"{context.Path}/ParameterSet/{parameter.Name}",
                    $"Parameter '{parameter.Name}' has dataSource='constant' but explicitly sets readOnly='false' — a constant data source should be readOnly.",
                    CandidateNumber: 27);
            }
        }
    }
}
