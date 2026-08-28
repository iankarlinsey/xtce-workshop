using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// The modeled constructs' bail-out paths: shapes the models deliberately refuse
/// (dynamic values, offsets, embedded comments, unexpected attributes) must fall back to
/// preserved fragments and round-trip byte-faithfully — never drop, never half-model.
/// </summary>
public class BailOutLosslessnessTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static void AssertRoundTrips(SpaceSystem loaded)
    {
        var written = XtceDocumentWriter.Write(loaded);
        Assert.Equal(loaded, Load(written));
    }

    [Test]
    public void DynamicEntryLocation_AndRepeatWithOffset_StayPreserved()
    {
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <TelemetryMetaData>
                <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
                <ParameterSet><Parameter name="P" parameterTypeRef="T"/></ParameterSet>
                <ContainerSet>
                  <SequenceContainer name="Frame">
                    <EntryList>
                      <ParameterRefEntry parameterRef="P">
                        <LocationInContainerInBits><DynamicValue><ParameterInstanceRef parameterRef="P"/></DynamicValue></LocationInContainerInBits>
                        <RepeatEntry><Count><FixedValue>3</FixedValue></Count><Offset><FixedValue>2</FixedValue></Offset></RepeatEntry>
                      </ParameterRefEntry>
                    </EntryList>
                  </SequenceContainer>
                </ContainerSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);

        var entry = loaded.TelemetryMetaData!.ContainerSet![0].EntryList[0];
        Assert.Null(entry.Location);
        Assert.Null(entry.Repeat);
        Assert.Equal(["LocationInContainerInBits", "RepeatEntry"],
            entry.Preserved!.Select(f => f.ElementName).ToList());
        AssertRoundTrips(loaded);
    }

    [Test]
    public void EntryIncludeCondition_WithComparisonList_ModelsTheList()
    {
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <TelemetryMetaData>
                <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
                <ParameterSet><Parameter name="P" parameterTypeRef="T"/></ParameterSet>
                <ContainerSet>
                  <SequenceContainer name="Frame">
                    <EntryList>
                      <ParameterRefEntry parameterRef="P">
                        <IncludeCondition><ComparisonList><Comparison parameterRef="P" value="1"/><Comparison parameterRef="P" value="2" comparisonOperator="!="/></ComparisonList></IncludeCondition>
                      </ParameterRefEntry>
                    </EntryList>
                  </SequenceContainer>
                </ContainerSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);

        var condition = loaded.TelemetryMetaData!.ContainerSet![0].EntryList[0].IncludeCondition!;
        Assert.Equal(2, condition.ComparisonList!.Count);
        Assert.Equal("!=", condition.ComparisonList[1].ComparisonOperator);
        AssertRoundTrips(loaded);
    }

    [Test]
    public void VerifierOddShapes_FallBackLosslessly()
    {
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <CommandMetaData>
                <MetaCommandSet>
                  <MetaCommand name="Cmd">
                    <VerifierSet>
                      <ExecutionVerifier>
                        <ContainerRef containerRef="C"><!-- child --></ContainerRef>
                        <CheckWindowAlgorithms><VerifierToTriggerOn verifierToTriggerOn="complete"/></CheckWindowAlgorithms>
                      </ExecutionVerifier>
                      <CompleteVerifier>
                        <Comparison parameterRef="Ack" value="1">
                          <!-- a child makes the plain form unmodelable -->
                        </Comparison>
                        <CheckWindow timeToStopChecking="PT5S"/>
                      </CompleteVerifier>
                    </VerifierSet>
                  </MetaCommand>
                </MetaCommandSet>
              </CommandMetaData>
            </SpaceSystem>
            """);

        var verifiers = loaded.CommandMetaData!.MetaCommands.Single().Verifiers!;
        // The ContainerRef with a child stays preserved.
        Assert.Null(verifiers[0].ContainerRef);
        Assert.Equal(["ContainerRef", "CheckWindowAlgorithms"],
            verifiers[0].Preserved!.Select(f => f.ElementName).ToList());
        // The Comparison with a child stays a preserved fragment; CheckWindow still models.
        Assert.Null(verifiers[1].Comparison);
        Assert.Equal(["Comparison"], verifiers[1].Preserved!.Select(f => f.ElementName).ToList());
        Assert.True(verifiers[1].HasCheckWindow);
        AssertRoundTrips(loaded);
    }

    [Test]
    public void ParameterToSet_UnmodelableNewValue_AndForeignListEntry_StayPreserved()
    {
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <CommandMetaData>
                <MetaCommandSet>
                  <MetaCommand name="Cmd">
                    <ParameterToSetList>
                      <ParameterToSet parameterRef="P"><NewValue><!-- odd -->7</NewValue></ParameterToSet>
                      <SomethingElse/>
                    </ParameterToSetList>
                  </MetaCommand>
                </MetaCommandSet>
              </CommandMetaData>
            </SpaceSystem>
            """);

        var sets = loaded.CommandMetaData!.MetaCommands.Single().ParameterToSets!;
        Assert.Null(sets[0].NewValue);
        Assert.Equal(["NewValue"], sets[0].Preserved!.Select(f => f.ElementName).ToList());
        Assert.Equal("SomethingElse", sets[1].RawXml!.ElementName);
        AssertRoundTrips(loaded);
    }

    [Test]
    public void AlgorithmText_WithUnexpectedAttribute_StaysPreserved()
    {
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <TelemetryMetaData>
                <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
                <ParameterSet/>
                <AlgorithmSet>
                  <CustomAlgorithm name="A">
                    <AlgorithmText language="python" weird="x">y = 1</AlgorithmText>
                  </CustomAlgorithm>
                </AlgorithmSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);

        var algorithm = loaded.TelemetryMetaData!.AlgorithmSet!.Single();
        Assert.Null(algorithm.AlgorithmText);
        Assert.Equal(["AlgorithmText"], algorithm.Preserved!.Select(f => f.ElementName).ToList());
        AssertRoundTrips(loaded);
    }

    [Test]
    public void TransmissionConstraint_InstanceRefComparison_StaysPreserved()
    {
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <CommandMetaData>
                <MetaCommandSet>
                  <MetaCommand name="Cmd">
                    <TransmissionConstraintList>
                      <TransmissionConstraint>
                        <Comparison value="1"><ParameterInstanceRef parameterRef="Mode"/></Comparison>
                      </TransmissionConstraint>
                      <Foreign/>
                    </TransmissionConstraintList>
                  </MetaCommand>
                </MetaCommandSet>
              </CommandMetaData>
            </SpaceSystem>
            """);

        var constraints = loaded.CommandMetaData!.MetaCommands.Single().TransmissionConstraints!;
        Assert.Null(constraints[0].Comparison);
        Assert.Equal(["Comparison"], constraints[0].Preserved!.Select(f => f.ElementName).ToList());
        Assert.Equal("Foreign", constraints[1].RawXml!.ElementName);
        AssertRoundTrips(loaded);
    }
}
