using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>R11 (no-dangling-name-references) and R10 (nextcontainer-ref-must-resolve).</summary>
public class DanglingReferenceRulesTests
{
    private const string R11 = "XTCE-1.2-R11-no-dangling-name-references";
    private const string R10 = "XTCE-1.2-R10-nextcontainer-ref-must-resolve";

    [Test]
    public void DanglingParameterTypeRef_IsFlagged()
    {
        var spaceSystem = new SpaceSystem("S", [], new TelemetryMetaData(
            [], [new Parameter("P", "NoSuchType")]));

        var issues = XtceValidator.Validate(spaceSystem);

        var issue = Assert.Single(issues, i => i.RuleId == R11);
        Assert.Contains("NoSuchType", issue.Message);
        Assert.Equal("S/ParameterSet/P", issue.Location);
    }

    [Test]
    public void ParameterTypeRefToPreservedUnmodeledType_IsNotFlagged()
    {
        var spaceSystem = new SpaceSystem("S", [], new TelemetryMetaData(
            [],
            [new Parameter("P", "Blob_Type", InitialValue: "cafebabe")],
            PreservedParameterTypes:
            [
                new RawXmlFragment("BinaryParameterType",
                    """<BinaryParameterType name="Blob_Type" xmlns="http://www.omg.org/spec/XTCE/20180204"/>"""),
            ]));

        var issues = XtceValidator.Validate(spaceSystem);

        // The type exists (opaquely): no dangling-ref finding, and R15 must not try to
        // check the initial value against a definition it can't inspect.
        Assert.Empty(issues);
    }

    [Test]
    public void ParameterTypeRefResolvingToAncestor_IsNotFlagged()
    {
        var child = new SpaceSystem("Child", [], new TelemetryMetaData(
            [], [new Parameter("P", "SharedType")]));
        var root = new SpaceSystem("Root", [child], new TelemetryMetaData(
            [new ParameterTypeDefinition("SharedType", ParameterTypeKind.Integer)], []));

        var issues = XtceValidator.Validate(root);

        Assert.DoesNotContain(issues, i => i.RuleId == R11);
    }

    [Test]
    public void DanglingEntryAndBaseContainerAndComparisonRefs_AreEachFlagged()
    {
        var container = new SequenceContainer(
            "Frame",
            [
                new SequenceEntry(SequenceEntryKind.ParameterRef, "MissingParam"),
                new SequenceEntry(SequenceEntryKind.ContainerRef, "MissingContainer"),
            ],
            new BaseContainer("MissingBase", new RestrictionCriteria(
                Comparison: new Comparison("MissingCompareParam", "1"))));
        var spaceSystem = new SpaceSystem("S", [], new TelemetryMetaData(
            [], [], ContainerSet: [container]));

        var issues = XtceValidator.Validate(spaceSystem).Where(i => i.RuleId == R11).ToList();

        Assert.Equal(4, issues.Count);
        Assert.Contains(issues, i => i.Message.Contains("MissingParam"));
        Assert.Contains(issues, i => i.Message.Contains("MissingContainer"));
        Assert.Contains(issues, i => i.Message.Contains("MissingBase"));
        Assert.Contains(issues, i => i.Message.Contains("MissingCompareParam"));
    }

    [Test]
    public void FullyResolvedContainerGraph_ProducesNoFindings()
    {
        var telemetry = new TelemetryMetaData(
            [new ParameterTypeDefinition("T", ParameterTypeKind.Integer)],
            [new Parameter("P", "T")],
            ContainerSet:
            [
                new SequenceContainer("Base", [], Abstract: true),
                new SequenceContainer("Sub", [new SequenceEntry(SequenceEntryKind.ParameterRef, "P")]),
                new SequenceContainer(
                    "Frame",
                    [new SequenceEntry(SequenceEntryKind.ContainerRef, "Sub")],
                    new BaseContainer("Base", new RestrictionCriteria(
                        Comparison: new Comparison("P", "1"),
                        NextContainerRef: "Sub"))),
            ]);
        var spaceSystem = new SpaceSystem("S", [], telemetry);

        var issues = XtceValidator.Validate(spaceSystem);

        Assert.Empty(issues);
    }

    [Test]
    public void DanglingNextContainerRef_IsFlaggedByR10NotR11()
    {
        var telemetry = new TelemetryMetaData(
            [],
            [new Parameter("P", "T")],
            ContainerSet:
            [
                new SequenceContainer("Base", []),
                new SequenceContainer("Frame", [], new BaseContainer("Base", new RestrictionCriteria(
                    Comparison: new Comparison("P", "1"),
                    NextContainerRef: "NoSuchContainer"))),
            ],
            PreservedParameterTypes:
            [
                new RawXmlFragment("BinaryParameterType",
                    """<BinaryParameterType name="T" xmlns="http://www.omg.org/spec/XTCE/20180204"/>"""),
            ]);
        var spaceSystem = new SpaceSystem("S", [], telemetry);

        var issues = XtceValidator.Validate(spaceSystem);

        var r10 = Assert.Single(issues, i => i.RuleId == R10);
        Assert.Contains("NoSuchContainer", r10.Message);
        Assert.Equal("S/ContainerSet/Frame", r10.Location);
        Assert.DoesNotContain(issues, i => i.RuleId == R11);
    }

    [Test]
    public void R15_ChecksInitialValueAgainstTypeDefinedInAncestor()
    {
        // The resolver upgrade's payoff: the bad initial value is caught even though the
        // type lives one SpaceSystem up.
        var child = new SpaceSystem("Child", [], new TelemetryMetaData(
            [], [new Parameter("P", "SharedType", InitialValue: "not-a-number")]));
        var root = new SpaceSystem("Root", [child], new TelemetryMetaData(
            [new ParameterTypeDefinition("SharedType", ParameterTypeKind.Integer)], []));

        var issues = XtceValidator.Validate(root);

        var issue = Assert.Single(issues, i => i.RuleId == "XTCE-1.2-R15-typed-value-valid-for-type");
        Assert.Equal("Root/Child/ParameterSet/P", issue.Location);
    }
}
