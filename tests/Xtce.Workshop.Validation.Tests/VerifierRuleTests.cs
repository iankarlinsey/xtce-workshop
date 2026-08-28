using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>R12: no duplicate Complete/Execution verifiers after BaseMetaCommand inheritance.</summary>
public class VerifierRuleTests
{
    private const string R12 = "XTCE-1.2-R12-no-duplicate-verifiers-post-inheritance";
    private const string R11 = "XTCE-1.2-R11-no-dangling-name-references";
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static CommandVerifier Verifier(string kind, string value) =>
        new(kind, Comparison: new Comparison("Ack", value), HasCheckWindow: true, TimeToStopChecking: "PT5S");

    private static SpaceSystem Document(params MetaCommand[] metaCommands) =>
        new("S", [], CommandMetaData: new CommandMetaData(metaCommands));

    [Test]
    public void DuplicateCompleteVerifierAcrossInheritance_IsFlagged()
    {
        var shared = Verifier("CompleteVerifier", "1");
        var issues = XtceValidator.Validate(Document(
            new MetaCommand("Base", Verifiers: [shared]),
            new MetaCommand("Child", BaseMetaCommandRef: "Base", Verifiers: [shared])));

        var issue = Assert.Single(issues, i => i.RuleId == R12);
        Assert.Contains("Duplicate CompleteVerifier", issue.Message);
        Assert.Equal("S/CommandMetaData/MetaCommandSet/Child", issue.Location);
    }

    [Test]
    public void WhitespaceDifferencesInPreservedChecks_StillCountAsDuplicates()
    {
        var compact = new CommandVerifier("ExecutionVerifier", Preserved:
            [new RawXmlFragment("BooleanExpression", $"""<BooleanExpression xmlns="{Ns}"><Condition><ParameterInstanceRef parameterRef="Ack"/><ComparisonOperator>==</ComparisonOperator><Value>1</Value></Condition></BooleanExpression>""")]);
        var spaced = new CommandVerifier("ExecutionVerifier", Preserved:
            [new RawXmlFragment("BooleanExpression", $"""
             <BooleanExpression xmlns="{Ns}">
               <Condition><ParameterInstanceRef parameterRef="Ack"/><ComparisonOperator>==</ComparisonOperator><Value>1</Value></Condition>
             </BooleanExpression>
             """)]);
        var issues = XtceValidator.Validate(Document(
            new MetaCommand("Cmd", Verifiers: [compact, spaced])));

        Assert.Single(issues, i => i.RuleId == R12);
    }

    [Test]
    public void DistinctVerifiersAcrossInheritance_AreClean()
    {
        var issues = XtceValidator.Validate(Document(
            new MetaCommand("Base", Verifiers: [Verifier("CompleteVerifier", "1")]),
            new MetaCommand("Child", BaseMetaCommandRef: "Base", Verifiers: [Verifier("CompleteVerifier", "2")])));

        Assert.DoesNotContain(issues, i => i.RuleId == R12);
    }

    [Test]
    public void DuplicateWithinOwnList_IsFlaggedEvenWithoutInheritance()
    {
        var issues = XtceValidator.Validate(Document(
            new MetaCommand("Cmd", Verifiers: [Verifier("CompleteVerifier", "1"), Verifier("CompleteVerifier", "1")])));

        Assert.Single(issues, i => i.RuleId == R12);
    }

    [Test]
    public void GrandparentChainDuplicates_AreFound()
    {
        var shared = Verifier("CompleteVerifier", "1");
        var issues = XtceValidator.Validate(Document(
            new MetaCommand("GrandBase", Verifiers: [shared]),
            new MetaCommand("Mid", BaseMetaCommandRef: "GrandBase"),
            new MetaCommand("Leaf", BaseMetaCommandRef: "Mid", Verifiers: [shared])));

        var issue = Assert.Single(issues, i => i.RuleId == R12);
        Assert.Contains("Leaf", issue.Location);
    }

    [Test]
    public void CyclicBaseChain_DoesNotHangAndStillDetectsOwnDuplicates()
    {
        var shared = Verifier("CompleteVerifier", "1");
        var issues = XtceValidator.Validate(Document(
            new MetaCommand("A", BaseMetaCommandRef: "B", Verifiers: [shared, shared]),
            new MetaCommand("B", BaseMetaCommandRef: "A")));

        Assert.Contains(issues, i => i.RuleId == R12 && i.Location.Contains("/A"));
    }

    [Test]
    public void DanglingBaseMetaCommandRef_IsFlaggedByR11()
    {
        var issues = XtceValidator.Validate(Document(
            new MetaCommand("Cmd", BaseMetaCommandRef: "NoSuchCmd")));

        var issue = Assert.Single(issues, i => i.RuleId == R11);
        Assert.Contains("NoSuchCmd", issue.Message);
    }

    [Test]
    public void BaseInParentSpaceSystem_IsResolvedForTheMerge()
    {
        var shared = Verifier("CompleteVerifier", "1");
        var child = new SpaceSystem("Child", [], CommandMetaData: new CommandMetaData(
            [new MetaCommand("Leaf", BaseMetaCommandRef: "Base", Verifiers: [shared])]));
        var root = new SpaceSystem("Root", [child], CommandMetaData: new CommandMetaData(
            [new MetaCommand("Base", Verifiers: [shared])]));

        var issues = XtceValidator.Validate(root);

        Assert.Single(issues, i => i.RuleId == R12);
        Assert.DoesNotContain(issues, i => i.RuleId == R11);
    }
}
