using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>
/// ArgumentScanner's remaining fragment duties: comparison forms inside preserved
/// constraint/verifier XML, and ParameterToSetList. Argument declarations, types, and
/// assignments are modeled — see <see cref="ModeledArgumentsTests"/>.
/// </summary>
public class ArgumentScannerTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    [Test]
    public void ParameterToSets_LoadModeled_WithLiteralAndDerivationEntries()
    {
        var xml = $"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <CommandMetaData>
                <MetaCommandSet>
                  <MetaCommand name="Cmd">
                    <ParameterToSetList>
                      <ParameterToSet parameterRef="P" setOnVerification="execution"><NewValue>7</NewValue></ParameterToSet>
                      <ParameterToSet parameterRef="Q"><Derivation><ParameterInstanceRefOperand parameterRef="Q"/><ValueOperand>1</ValueOperand><Operator>+</Operator></Derivation></ParameterToSet>
                    </ParameterToSetList>
                  </MetaCommand>
                </MetaCommandSet>
              </CommandMetaData>
            </SpaceSystem>
            """;
        var document = Xtce.Workshop.Model.XtceDocumentReader.Load(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)));

        var parameterToSets = document.CommandMetaData!.MetaCommands.Single().ParameterToSets!;
        Assert.Equal(2, parameterToSets.Count);
        Assert.Equal(("P", "7", "execution"),
            (parameterToSets[0].ParameterRef, parameterToSets[0].NewValue, parameterToSets[0].SetOnVerification));
        Assert.Null(parameterToSets[1].NewValue); // Derivation-based — no literal
        Assert.Equal(["Derivation"], parameterToSets[1].Preserved!.Select(f => f.ElementName).ToList());
    }

    [Test]
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

    [Test]
    public void ScanComparisons_PlainComparisonWithParameterRefAttribute_IsThePlainForm()
    {
        var xml = $"""<CompleteVerifier xmlns="{Ns}"><Comparison parameterRef="Ack" value="1"/><CheckWindow timeToStopChecking="PT5S"/></CompleteVerifier>""";

        var comparison = Assert.Single(ArgumentScanner.ScanComparisons(xml));

        Assert.Equal(ArgumentScanner.ComparisonForm.Plain, comparison.Form);
        Assert.Equal("Ack", comparison.ParameterRef);
        Assert.Equal("1", comparison.Value);
    }
}
