using Xtce.Workshop.Model;
using Xunit;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>R09: a Message's ContainerRef must reference a root-level container.</summary>
public class MessageRuleTests
{
    private const string R09 = "XTCE-1.2-R09-messagetype-containerref-must-be-root";

    private static SpaceSystem BuildDocument(params Message[] messages)
    {
        var telemetry = new TelemetryMetaData(
            [new ParameterTypeDefinition("T", ParameterTypeKind.Integer)],
            [new Parameter("P", "T")],
            ContainerSet:
            [
                new SequenceContainer("AbstractBase", [], Abstract: true),
                new SequenceContainer(
                    "WholePacket",
                    [new SequenceEntry(SequenceEntryKind.ContainerRef, "Piece")],
                    new BaseContainer("AbstractBase", new RestrictionCriteria(
                        Comparison: new Comparison("P", "1")))),
                new SequenceContainer("Piece", [new SequenceEntry(SequenceEntryKind.ParameterRef, "P")]),
            ],
            MessageSet: new MessageSet(messages));
        return new SpaceSystem("S", [], telemetry);
    }

    [Fact]
    public void MessageTargetingAWholePacket_IsClean()
    {
        var issues = XtceValidator.Validate(BuildDocument(new Message("M", "WholePacket")));

        Assert.DoesNotContain(issues, i => i.RuleId == R09);
    }

    [Fact]
    public void MessageTargetingAnAbstractContainer_IsFlagged()
    {
        var issues = XtceValidator.Validate(BuildDocument(new Message("M", "AbstractBase")));

        var issue = Assert.Single(issues, i => i.RuleId == R09);
        Assert.Contains("abstract", issue.Message);
        Assert.Equal("S/MessageSet/M", issue.Location);
    }

    [Fact]
    public void MessageTargetingASubPieceContainer_IsFlagged()
    {
        var issues = XtceValidator.Validate(BuildDocument(new Message("M", "Piece")));

        var issue = Assert.Single(issues, i => i.RuleId == R09);
        Assert.Contains("sub-piece", issue.Message);
    }

    [Fact]
    public void MessageWithUnresolvableContainerRef_IsFlaggedByR09NotR11()
    {
        var issues = XtceValidator.Validate(BuildDocument(new Message("M", "Nowhere")));

        var issue = Assert.Single(issues, i => i.RuleId == R09);
        Assert.Contains("does not resolve", issue.Message);
        Assert.DoesNotContain(issues, i => i.RuleId == "XTCE-1.2-R11-no-dangling-name-references");
    }

    [Fact]
    public void MessageInAChildSystem_ResolvesFromItsOwnScope()
    {
        var child = new SpaceSystem("Child", [], new TelemetryMetaData(
            [], [],
            ContainerSet: [new SequenceContainer("LocalPacket", [])],
            MessageSet: new MessageSet([new Message("M", "LocalPacket")])));
        var root = new SpaceSystem("Root", [child]);

        var issues = XtceValidator.Validate(root);

        Assert.DoesNotContain(issues, i => i.RuleId == R09);
    }
}
