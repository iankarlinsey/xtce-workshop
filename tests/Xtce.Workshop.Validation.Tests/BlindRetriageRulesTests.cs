using Xtce.Workshop.Model;
using Xunit;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>R22 and R23 — the rules recovered by the blind re-triage (issue #48).</summary>
public class BlindRetriageRulesTests
{
    private const string R22 = "XTCE-1.2-R22-fixedvalue-bitlength-sufficient";
    private const string R23 = "XTCE-1.2-R23-constant-datasource-should-be-readonly";
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem WithFixedValueEntry(string binaryValue, int sizeInBits)
    {
        var container = new CommandContainer("Frame", Preserved:
        [
            new RawXmlFragment("EntryList",
                $"""<EntryList xmlns="{Ns}"><FixedValueEntry binaryValue="{binaryValue}" sizeInBits="{sizeInBits}"/></EntryList>"""),
        ]);
        return new SpaceSystem("S", [], CommandMetaData: new CommandMetaData(
            [new MetaCommand("Cmd", CommandContainer: container)]));
    }

    [Theory]
    [InlineData("5A", 8)]      // exactly enough
    [InlineData("1ACF", 12)]   // more than enough — MSB truncation is legal
    public void SufficientOrOversizedBinaryValue_IsClean(string binaryValue, int sizeInBits)
    {
        var issues = XtceValidator.Validate(WithFixedValueEntry(binaryValue, sizeInBits));

        Assert.DoesNotContain(issues, i => i.RuleId == R22);
    }

    [Fact]
    public void InsufficientBinaryValue_IsFlaggedAsWarning()
    {
        var issues = XtceValidator.Validate(WithFixedValueEntry("5A", 16));

        var issue = Assert.Single(issues, i => i.RuleId == R22);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Contains("8 bits", issue.Message);
        Assert.Contains("16", issue.Message);
    }

    private static SpaceSystem WithProperties(string attributes) =>
        new("S", [], new TelemetryMetaData(
            [new ParameterTypeDefinition("T", ParameterTypeKind.Integer)],
            [new Parameter("P", "T", Preserved:
                [new RawXmlFragment("ParameterProperties", $"""<ParameterProperties {attributes} xmlns="{Ns}"/>""")])]));

    [Fact]
    public void ConstantWithExplicitReadOnlyFalse_IsFlaggedAsWarning()
    {
        var issues = XtceValidator.Validate(WithProperties("""dataSource="constant" readOnly="false" """));

        var issue = Assert.Single(issues, i => i.RuleId == R23);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
    }

    [Theory]
    [InlineData("""dataSource="constant" readOnly="true" """)] // correct combination
    [InlineData("""dataSource="constant" """)]                 // absent readOnly: implicit enforcement blessed by the doc
    [InlineData("""dataSource="telemetered" readOnly="false" """)] // not constant
    public void OtherCombinations_AreClean(string attributes)
    {
        var issues = XtceValidator.Validate(WithProperties(attributes));

        Assert.DoesNotContain(issues, i => i.RuleId == R23);
    }
}
