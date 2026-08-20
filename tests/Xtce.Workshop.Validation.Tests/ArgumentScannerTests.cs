using Xtce.Workshop.Model;
using Xunit;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>
/// ArgumentScanner: the fragment-parsing layer that lets R05/R07/R15 evaluate
/// command-argument candidate sites without expanding the object model.
/// </summary>
public class ArgumentScannerTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static CommandMetaData WithArgumentTypeSet(string body) => new([], Preserved:
        [new RawXmlFragment("ArgumentTypeSet", $"""<ArgumentTypeSet xmlns="{Ns}">{body}</ArgumentTypeSet>""")]);

    [Fact]
    public void ScanArgumentTypes_ReadsScalarAttributes_AndAdjacentSiblings()
    {
        // Two back-to-back empty elements — the regression case for ReadOuterXml's
        // reader-advance semantics (a naive loop skips every other sibling).
        var commandMetaData = WithArgumentTypeSet(
            """<IntegerArgumentType name="U8" signed="false" sizeInBits="8" initialValue="7"/>""" +
            """<FloatArgumentType name="F" initialValue="1.5"/>""" +
            """<BooleanArgumentType name="B" oneStringValue="ON" zeroStringValue="OFF"/>""" +
            """<RelativeTimeAgumentType name="RT"/>""");

        var types = ArgumentScanner.ScanArgumentTypes(commandMetaData);

        Assert.Equal(4, types.Count);
        var u8 = types.Single(t => t.Name == "U8");
        Assert.Equal(ParameterTypeKind.Integer, u8.Kind);
        Assert.False(u8.Signed);
        Assert.Equal(8, u8.SizeInBits);
        Assert.Equal("7", u8.InitialValue);
        Assert.Equal(ParameterTypeKind.Float, types.Single(t => t.Name == "F").Kind);
        var boolean = types.Single(t => t.Name == "B");
        Assert.Equal("ON", boolean.OneStringValue);
        Assert.Equal("OFF", boolean.ZeroStringValue);
        // The XSD's own typo'd element name maps to RelativeTime.
        Assert.Equal(ParameterTypeKind.RelativeTime, types.Single(t => t.Name == "RT").Kind);
    }

    [Fact]
    public void ScanArgumentTypes_ReadsEnumerationLabels_AndArrayDimensions()
    {
        var commandMetaData = WithArgumentTypeSet(
            """<EnumeratedArgumentType name="Mode" initialValue="SAFE"><EnumerationList><Enumeration value="0" label="SAFE"/><Enumeration value="1" label="ACTIVE"/></EnumerationList></EnumeratedArgumentType>""" +
            """<ArrayArgumentType name="Arr" arrayTypeRef="U8"><DimensionList><Dimension><StartingIndex><FixedValue>0</FixedValue></StartingIndex><EndingIndex><FixedValue>3</FixedValue></EndingIndex></Dimension></DimensionList></ArrayArgumentType>""");

        var types = ArgumentScanner.ScanArgumentTypes(commandMetaData);

        var mode = types.Single(t => t.Name == "Mode");
        Assert.Equal(["SAFE", "ACTIVE"], (mode.Enumerations ?? []).Select(e => e.Label));
        var array = types.Single(t => t.Name == "Arr");
        Assert.Equal(ParameterTypeKind.Array, array.Kind);
        Assert.Equal("U8", array.ArrayTypeRef);
        var dimension = Assert.Single(array.Dimensions ?? []);
        Assert.Equal(0, dimension.StartingIndex.FixedValue);
        Assert.Equal(3, dimension.EndingIndex.FixedValue);
    }

    [Fact]
    public void MergedArguments_WalksTheBaseMetaCommandChain_ParentTypesResolveFromParentScope()
    {
        var baseCommand = new MetaCommand("Base", Abstract: true, Preserved:
            [new RawXmlFragment("ArgumentList", $"""<ArgumentList xmlns="{Ns}"><Argument name="A" argumentTypeRef="U8"/></ArgumentList>""")]);
        var child = new MetaCommand("Child", BaseMetaCommandRef: "Base", Preserved:
            [new RawXmlFragment("ArgumentList", $"""<ArgumentList xmlns="{Ns}"><Argument name="B" argumentTypeRef="U8" initialValue="1"/></ArgumentList>""")]);
        var document = new SpaceSystem("S", [], CommandMetaData: new CommandMetaData([baseCommand, child], Preserved:
            [new RawXmlFragment("ArgumentTypeSet", $"""<ArgumentTypeSet xmlns="{Ns}"><IntegerArgumentType name="U8" signed="false" sizeInBits="8"/></ArgumentTypeSet>""")]));
        var context = SpaceSystemContext.Build(document);

        var merged = ArgumentScanner.MergedArguments(context, child);

        Assert.Equal(["B", "A"], merged.Select(a => a.Decl.Name));
        Assert.All(merged, a => Assert.NotNull(ArgumentScanner.ResolveArgumentType(a.Scope, a.Decl.TypeRef)));
    }

    [Fact]
    public void ResolveArgumentType_FallsBackToAncestors_AndSkipsPathQualifiedRefs()
    {
        var childSystem = new SpaceSystem("Child", []);
        var root = new SpaceSystem("Root", [childSystem], CommandMetaData: new CommandMetaData([], Preserved:
            [new RawXmlFragment("ArgumentTypeSet", $"""<ArgumentTypeSet xmlns="{Ns}"><IntegerArgumentType name="U8"/></ArgumentTypeSet>""")]));
        var childContext = SpaceSystemContext.Build(root).ChildrenByName["Child"];

        Assert.NotNull(ArgumentScanner.ResolveArgumentType(childContext, "U8"));
        Assert.Null(ArgumentScanner.ResolveArgumentType(childContext, "/Root/U8"));
        Assert.Null(ArgumentScanner.ResolveArgumentType(childContext, "NoSuchType"));
    }

    [Fact]
    public void ScanArgumentAssignments_And_ParameterToSets_ReadTheirLists()
    {
        var metaCommand = new MetaCommand("Cmd",
            BaseMetaCommandRef: "Base",
            BaseMetaCommandPreserved:
            [
                new RawXmlFragment("ArgumentAssignmentList",
                    $"""<ArgumentAssignmentList xmlns="{Ns}"><ArgumentAssignment argumentName="A" argumentValue="42"/></ArgumentAssignmentList>"""),
            ],
            Preserved:
            [
                new RawXmlFragment("ParameterToSetList",
                    $"""<ParameterToSetList xmlns="{Ns}"><ParameterToSet parameterRef="P"><NewValue>7</NewValue></ParameterToSet><ParameterToSet parameterRef="Q"><Derivation><TriggeredMathOperation outputParameterRef="Q"/></Derivation></ParameterToSet></ParameterToSetList>"""),
            ]);

        var assignment = Assert.Single(ArgumentScanner.ScanArgumentAssignments(metaCommand));
        Assert.Equal(("A", "42"), (assignment.ArgumentName, assignment.ArgumentValue));

        var parameterToSets = ArgumentScanner.ScanParameterToSets(metaCommand);
        Assert.Equal(2, parameterToSets.Count);
        Assert.Equal("7", parameterToSets[0].NewValue);
        Assert.Null(parameterToSets[1].NewValue); // Derivation-based — no literal
    }

    [Fact]
    public void ScanComparisons_DistinguishesAllThreeForms_AndSkipsInstanceRefRhs()
    {
        var xml = $"""
            <TransmissionConstraintList xmlns="{Ns}">
              <TransmissionConstraint>
                <Comparison value="1"><ArgumentInstanceRef argumentRef="A"/></Comparison>
              </TransmissionConstraint>
              <TransmissionConstraint>
                <Comparison value="2"><ParameterInstanceRef parameterRef="P"/></Comparison>
              </TransmissionConstraint>
              <TransmissionConstraint>
                <BooleanExpression>
                  <Condition><ArgumentInstanceRef argumentRef="A"/><ComparisonOperator>==</ComparisonOperator><Value>3</Value></Condition>
                </BooleanExpression>
              </TransmissionConstraint>
              <TransmissionConstraint>
                <BooleanExpression>
                  <Condition><ParameterInstanceRef parameterRef="P"/><ComparisonOperator>==</ComparisonOperator><ParameterInstanceRef parameterRef="Q"/></Condition>
                </BooleanExpression>
              </TransmissionConstraint>
            </TransmissionConstraintList>
            """;

        var comparisons = ArgumentScanner.ScanComparisons(xml);

        Assert.Equal(3, comparisons.Count); // the instance-ref-vs-instance-ref Condition has no literal
        Assert.Equal(("A", "1", ArgumentScanner.ComparisonForm.InstanceRef),
            (comparisons[0].ArgumentRef, comparisons[0].Value, comparisons[0].Form));
        Assert.Equal(("P", "2", ArgumentScanner.ComparisonForm.InstanceRef),
            (comparisons[1].ParameterRef, comparisons[1].Value, comparisons[1].Form));
        Assert.Equal(("A", "3", ArgumentScanner.ComparisonForm.ConditionValue),
            (comparisons[2].ArgumentRef, comparisons[2].Value, comparisons[2].Form));
    }

    [Fact]
    public void ScanComparisons_PlainComparisonWithParameterRefAttribute_IsThePlainForm()
    {
        var xml = $"""<CompleteVerifier xmlns="{Ns}"><Comparison parameterRef="Ack" value="1"/><CheckWindow timeToStopChecking="PT5S"/></CompleteVerifier>""";

        var comparison = Assert.Single(ArgumentScanner.ScanComparisons(xml));

        Assert.Equal(ArgumentScanner.ComparisonForm.Plain, comparison.Form);
        Assert.Equal("Ack", comparison.ParameterRef);
        Assert.Equal("1", comparison.Value);
    }
}
