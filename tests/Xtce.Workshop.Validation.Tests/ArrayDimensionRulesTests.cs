using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>R02/R05/R06 (dimensions) plus the R11/R15 reach extensions.</summary>
public class ArrayDimensionRulesTests
{
    private const string R02 = "XTCE-1.2-R02-array-dim-count-match-type";
    private const string R05 = "XTCE-1.2-R05-dim-subset-lt-type";
    private const string R06 = "XTCE-1.2-R06-dimensionlist-order-must-ascend";
    private const string R11 = "XTCE-1.2-R11-no-dangling-name-references";
    private const string R15 = "XTCE-1.2-R15-typed-value-valid-for-type";
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static Dimension Dim(long start, long end) =>
        new(new DimensionIndex(start), new DimensionIndex(end));

    private static ParameterTypeDefinition MatrixType(params Dimension[] dims) =>
        new("Matrix_Type", ParameterTypeKind.Array, ArrayTypeRef: "Elem_Type", Dimensions: dims);

    private static SequenceEntry ArrayEntry(params (long Start, long End)[] dims)
    {
        var dimXml = string.Join("", dims.Select(d =>
            $"<Dimension><StartingIndex><FixedValue>{d.Start}</FixedValue></StartingIndex><EndingIndex><FixedValue>{d.End}</FixedValue></EndingIndex></Dimension>"));
        var list = dims.Length == 0 ? "" : $"<DimensionList>{dimXml}</DimensionList>";
        return new SequenceEntry(SequenceEntryKind.Raw, RawXml: new RawXmlFragment(
            "ArrayParameterRefEntry",
            $"""<ArrayParameterRefEntry parameterRef="Matrix" xmlns="{Ns}">{list}</ArrayParameterRefEntry>"""));
    }

    private static SpaceSystem Document(ParameterTypeDefinition arrayType, params SequenceEntry[] entries)
    {
        var telemetry = new TelemetryMetaData(
            [new ParameterTypeDefinition("Elem_Type", ParameterTypeKind.Integer), arrayType],
            [new Parameter("Matrix", arrayType.Name)],
            ContainerSet: [new SequenceContainer("Frame", entries)]);
        return new SpaceSystem("S", [], telemetry);
    }

    [Test]
    public void EntryDimensionCountMismatch_IsFlaggedByR02()
    {
        var issues = XtceValidator.Validate(Document(MatrixType(Dim(0, 3), Dim(0, 1)), ArrayEntry((0, 2))));

        var issue = Assert.Single(issues, i => i.RuleId == R02);
        Assert.Contains("1 dimension(s)", issue.Message);
        Assert.Contains("declares 2", issue.Message);
    }

    [Test]
    public void EntryWithoutDimensionList_IsClean_FullArrayAssumed()
    {
        var issues = XtceValidator.Validate(Document(MatrixType(Dim(0, 3)), ArrayEntry()));

        Assert.DoesNotContain(issues, i => i.RuleId == R02 || i.RuleId == R05);
    }

    [Test]
    public void ProperSubset_IsClean()
    {
        var issues = XtceValidator.Validate(Document(MatrixType(Dim(0, 3)), ArrayEntry((0, 2))));

        Assert.DoesNotContain(issues, i => i.RuleId == R05);
    }

    [Test]
    public void EntryBoundExceedingType_IsFlaggedByR05()
    {
        var issues = XtceValidator.Validate(Document(MatrixType(Dim(0, 3)), ArrayEntry((0, 9))));

        var issue = Assert.Single(issues, i => i.RuleId == R05);
        Assert.Contains("exceeds", issue.Message);
    }

    [Test]
    public void SameSizeAsType_IsFlaggedByR05_NotASubset()
    {
        var issues = XtceValidator.Validate(Document(MatrixType(Dim(0, 3)), ArrayEntry((0, 3))));

        var issue = Assert.Single(issues, i => i.RuleId == R05);
        Assert.Contains("not a subset", issue.Message);
    }

    [Test]
    public void DescendingDimensionOnType_IsFlaggedByR06()
    {
        var issues = XtceValidator.Validate(Document(MatrixType(Dim(5, 2))));

        var issue = Assert.Single(issues, i => i.RuleId == R06);
        Assert.Contains("must ascend", issue.Message);
        Assert.Contains("ParameterTypeSet/Matrix_Type", issue.Location);
    }

    [Test]
    public void DescendingDimensionOnEntry_IsFlaggedByR06()
    {
        var issues = XtceValidator.Validate(Document(MatrixType(Dim(0, 9)), ArrayEntry((5, 2))));

        Assert.Contains(issues, i => i.RuleId == R06 && i.Location.Contains("ContainerSet/Frame"));
    }

    [Test]
    public void DanglingArrayTypeRefAndMemberTypeRef_AreFlaggedByR11()
    {
        var telemetry = new TelemetryMetaData(
            [
                new ParameterTypeDefinition("Arr", ParameterTypeKind.Array, ArrayTypeRef: "NoSuchElem"),
                new ParameterTypeDefinition("Agg", ParameterTypeKind.Aggregate,
                    Members: [new Member("field", "NoSuchType")]),
            ],
            []);
        var issues = XtceValidator.Validate(new SpaceSystem("S", [], telemetry))
            .Where(i => i.RuleId == R11).ToList();

        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.Message.Contains("NoSuchElem"));
        Assert.Contains(issues, i => i.Message.Contains("NoSuchType"));
    }

    [Test]
    public void BadMemberInitialValue_IsFlaggedByR15()
    {
        var telemetry = new TelemetryMetaData(
            [
                new ParameterTypeDefinition("Elem_Type", ParameterTypeKind.Integer),
                new ParameterTypeDefinition("Agg", ParameterTypeKind.Aggregate,
                    Members:
                    [
                        new Member("good", "Elem_Type", "42"),
                        new Member("bad", "Elem_Type", "not-a-number"),
                    ]),
            ],
            []);
        var issues = XtceValidator.Validate(new SpaceSystem("S", [], telemetry))
            .Where(i => i.RuleId == R15).ToList();

        var issue = Assert.Single(issues);
        Assert.Equal("S/ParameterTypeSet/Agg/bad", issue.Location);
    }

    [Test]
    public void BadComparisonValue_IsFlaggedByR15()
    {
        var telemetry = new TelemetryMetaData(
            [new ParameterTypeDefinition("Elem_Type", ParameterTypeKind.Integer)],
            [new Parameter("P", "Elem_Type")],
            ContainerSet:
            [
                new SequenceContainer("Base", []),
                new SequenceContainer("Sub", [], new BaseContainer("Base", new RestrictionCriteria(
                    Comparison: new Comparison("P", "not-a-number")))),
            ]);
        var issues = XtceValidator.Validate(new SpaceSystem("S", [], telemetry))
            .Where(i => i.RuleId == R15).ToList();

        var issue = Assert.Single(issues);
        Assert.Contains("Comparison against 'P'", issue.Message);
    }
}
