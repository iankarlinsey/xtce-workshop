using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R22 (warning): a FixedValueEntry's binaryValue "should have sufficient bit
/// length to accomodate the size in bits" (ArgumentFixedValueEntryType/binaryValue, XSD
/// line 309 — candidate #3). Oversize is legal — the doc defines most-significant-bit
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
        // Modeled FixedValueEntry rows in command container entry lists (#97).
        foreach (var metaCommand in context.Node.CommandMetaData?.MetaCommands ?? [])
        {
            foreach (var entry in metaCommand.CommandContainer?.EntryList ?? [])
            {
                if (entry.Kind != SequenceEntryKind.FixedValue || entry.BinaryValue is null || entry.SizeInBits is null)
                {
                    continue;
                }
                var location = $"{context.Path}/CommandMetaData/MetaCommandSet/{metaCommand.Name}/CommandContainer";
                foreach (var issue in Check(entry.BinaryValue, entry.SizeInBits.Value, location))
                {
                    yield return issue;
                }
            }
        }

        // FixedValueEntry inside still-preserved fragments (e.g. command-side container sets).
        foreach (var (fragment, location) in FragmentEnumerator.EnumerateNode(context))
        {
            foreach (var entry in XmlFragmentInspector.FindFixedValueEntries(fragment.OuterXml))
            {
                if (entry.BinaryValue is null || entry.SizeInBits is null)
                {
                    continue;
                }
                foreach (var issue in Check(entry.BinaryValue, entry.SizeInBits.Value, location))
                {
                    yield return issue;
                }
            }
        }
    }

    private IEnumerable<ValidationIssue> Check(string binaryValue, long sizeInBits, string location)
    {
        var availableBits = (long)binaryValue.Length * 4; // hexBinary: 4 bits per digit
        if (availableBits < sizeInBits)
        {
            yield return new ValidationIssue(RuleId, Severity, location,
                $"FixedValueEntry binaryValue '{binaryValue}' provides {availableBits} bits but sizeInBits is {sizeInBits} — the value should have sufficient bit length.",
                CandidateNumber: 3);
        }
    }
}

/// <summary>
/// XTCE-1.2-R23 (warning): "For a constant data source, then 'readOnly' should be 'true'"
/// (ParameterPropertiesType/readOnly, XSD line 1040 — candidate #27). Only an EXPLICIT readOnly="false" alongside
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
            string? dataSource;
            string? readOnly;
            if (parameter.Properties is { } modeled)
            {
                dataSource = modeled.DataSource;
                readOnly = modeled.ReadOnly is { } value ? (value ? "true" : "false") : null;
            }
            else if ((parameter.Preserved ?? []).FirstOrDefault(f => f.ElementName == "ParameterProperties") is { } properties)
            {
                dataSource = XmlFragmentInspector.RootAttribute(properties.OuterXml, "dataSource");
                readOnly = XmlFragmentInspector.RootAttribute(properties.OuterXml, "readOnly");
            }
            else
            {
                continue;
            }
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
