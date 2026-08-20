using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>R12: no duplicate Complete/Execution verifiers after BaseMetaCommand inheritance.</summary>
public class VerifierRuleTests
{
    private const string R12 = "XTCE-1.2-R12-no-duplicate-verifiers-post-inheritance";
    private const string R11 = "XTCE-1.2-R11-no-dangling-name-references";
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static RawXmlFragment Verifier(string kind, string value) => new(kind,
        $"""<{kind} xmlns="{Ns}"><Comparison parameterRef="Ack" value="{value}"/><CheckWindow timeToStopChecking="PT5S"/></{kind}>""");

    private static SpaceSystem Document(params MetaCommand[] metaCommands) =>
        new("S", [], CommandMetaData: new CommandMetaData(metaCommands));

    [Test]
    public void DuplicateCompleteVerifierAcrossInheritance_IsFlagged()
    {
        var shared = Verifier("CompleteVerifier", "1");
        var issues = XtceValidator.Validate(Document(
            new MetaCommand("Base", CompleteVerifiers: [shared]),
            new MetaCommand("Child", BaseMetaCommandRef: "Base", CompleteVerifiers: [shared])));

        var issue = Assert.Single(issues, i => i.RuleId == R12);
        Assert.Contains("Duplicate CompleteVerifier", issue.Message);
        Assert.Equal("S/CommandMetaData/MetaCommandSet/Child", issue.Location);
    }

    [Test]
    public void WhitespaceDifferencesStillCountAsDuplicates()
    {
        var compact = new RawXmlFragment("ExecutionVerifier",
            $"""<ExecutionVerifier xmlns="{Ns}"><Comparison parameterRef="Ack" value="1"/><CheckWindow timeToStopChecking="PT5S"/></ExecutionVerifier>""");
        var spaced = new RawXmlFragment("ExecutionVerifier",
            $"""
             <ExecutionVerifier xmlns="{Ns}">
               <Comparison parameterRef="Ack" value="1"/>
               <CheckWindow timeToStopChecking="PT5S"/>
             </ExecutionVerifier>
             """);
        var issues = XtceValidator.Validate(Document(
            new MetaCommand("Cmd", ExecutionVerifiers: [compact, spaced])));

        Assert.Single(issues, i => i.RuleId == R12);
    }

    [Test]
    public void DistinctVerifiersAcrossInheritance_AreClean()
    {
        var issues = XtceValidator.Validate(Document(
            new MetaCommand("Base", CompleteVerifiers: [Verifier("CompleteVerifier", "1")]),
            new MetaCommand("Child", BaseMetaCommandRef: "Base", CompleteVerifiers: [Verifier("CompleteVerifier", "2")])));

        Assert.DoesNotContain(issues, i => i.RuleId == R12);
    }

    [Test]
    public void DuplicateWithinOwnList_IsFlaggedEvenWithoutInheritance()
    {
        var issues = XtceValidator.Validate(Document(
            new MetaCommand("Cmd", CompleteVerifiers: [Verifier("CompleteVerifier", "1"), Verifier("CompleteVerifier", "1")])));

        Assert.Single(issues, i => i.RuleId == R12);
    }

    [Test]
    public void GrandparentChainDuplicates_AreFound()
    {
        var shared = Verifier("CompleteVerifier", "1");
        var issues = XtceValidator.Validate(Document(
            new MetaCommand("GrandBase", CompleteVerifiers: [shared]),
            new MetaCommand("Mid", BaseMetaCommandRef: "GrandBase"),
            new MetaCommand("Leaf", BaseMetaCommandRef: "Mid", CompleteVerifiers: [shared])));

        var issue = Assert.Single(issues, i => i.RuleId == R12);
        Assert.Contains("Leaf", issue.Location);
    }

    [Test]
    public void CyclicBaseChain_DoesNotHangAndStillDetectsOwnDuplicates()
    {
        var shared = Verifier("CompleteVerifier", "1");
        var issues = XtceValidator.Validate(Document(
            new MetaCommand("A", BaseMetaCommandRef: "B", CompleteVerifiers: [shared, shared]),
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
            [new MetaCommand("Leaf", BaseMetaCommandRef: "Base", CompleteVerifiers: [shared])]));
        var root = new SpaceSystem("Root", [child], CommandMetaData: new CommandMetaData(
            [new MetaCommand("Base", CompleteVerifiers: [shared])]));

        var issues = XtceValidator.Validate(root);

        Assert.Single(issues, i => i.RuleId == R12);
        Assert.DoesNotContain(issues, i => i.RuleId == R11);
    }
}
