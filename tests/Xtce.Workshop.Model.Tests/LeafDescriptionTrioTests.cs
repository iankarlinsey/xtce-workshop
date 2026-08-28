using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Description trio on the leaf records (#123): Arguments, aggregate Members, and
/// command verifiers gain the shared Description record.
/// </summary>
public class LeafDescriptionTrioTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string RoundTrip(SpaceSystem loaded, out SpaceSystem reloaded)
    {
        var xml = XtceDocumentWriter.Write(loaded);
        reloaded = Load(xml);
        return xml;
    }

    [Test]
    public void Load_ModelsTrioOnArguments()
    {
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <CommandMetaData>
                <ArgumentTypeSet>
                  <IntegerArgumentType name="U8" signed="false" sizeInBits="8"/>
                </ArgumentTypeSet>
                <MetaCommandSet>
                  <MetaCommand name="Reboot">
                    <ArgumentList>
                      <Argument name="delay" argumentTypeRef="U8" shortDescription="restart delay">
                        <LongDescription>Seconds to wait before restarting.</LongDescription>
                        <AliasSet><Alias nameSpace="ops" alias="RESTART_DELAY"/></AliasSet>
                      </Argument>
                    </ArgumentList>
                  </MetaCommand>
                </MetaCommandSet>
              </CommandMetaData>
            </SpaceSystem>
            """);
        var argument = Assert.Single(loaded.CommandMetaData!.MetaCommands.Single().Arguments!);

        Assert.Equal("Seconds to wait before restarting.", argument.Description!.LongDescription);
        Assert.Equal("RESTART_DELAY", Assert.Single(argument.Description.Aliases!).Alias);
        Assert.Null(argument.Preserved);
        Assert.Equal("restart delay",
            Assert.Single(argument.PreservedAttributes!).Value);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_ModelsTrioOnAggregateMembers()
    {
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <TelemetryMetaData>
                <ParameterTypeSet>
                  <IntegerParameterType name="U16" signed="false" sizeInBits="16">
                    <UnitSet/>
                  </IntegerParameterType>
                  <AggregateParameterType name="Housekeeping">
                    <MemberList>
                      <Member name="voltage" typeRef="U16">
                        <LongDescription>Bus voltage in raw counts.</LongDescription>
                      </Member>
                      <Member name="current" typeRef="U16"/>
                    </MemberList>
                  </AggregateParameterType>
                </ParameterTypeSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);
        var members = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Housekeeping").Members!;

        Assert.Equal("Bus voltage in raw counts.", members[0].Description!.LongDescription);
        Assert.Null(members[0].Preserved);
        Assert.Null(members[1].Description);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_ModelsTrioOnVerifiers()
    {
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <TelemetryMetaData>
                <ParameterTypeSet>
                  <IntegerParameterType name="AckType" signed="false" sizeInBits="8">
                    <UnitSet/>
                  </IntegerParameterType>
                </ParameterTypeSet>
                <ParameterSet>
                  <Parameter name="Ack" parameterTypeRef="AckType"/>
                </ParameterSet>
              </TelemetryMetaData>
              <CommandMetaData>
                <MetaCommandSet>
                  <MetaCommand name="Reboot">
                    <VerifierSet>
                      <CompleteVerifier>
                        <LongDescription>Acknowledged by the flight computer.</LongDescription>
                        <Comparison parameterRef="Ack" value="1"/>
                        <CheckWindow timeToStopChecking="PT5S"/>
                      </CompleteVerifier>
                    </VerifierSet>
                  </MetaCommand>
                </MetaCommandSet>
              </CommandMetaData>
            </SpaceSystem>
            """);
        var verifier = Assert.Single(loaded.CommandMetaData!.MetaCommands.Single().Verifiers!);

        Assert.Equal("Acknowledged by the flight computer.", verifier.Description!.LongDescription);
        Assert.Null(verifier.Preserved);
        Assert.Equal("Ack", verifier.Comparison!.ParameterRef);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_AliasSetWithComment_StaysLosslessOnArguments()
    {
        // The shared trio helper's comment handling applies on the new spots too.
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <CommandMetaData>
                <ArgumentTypeSet>
                  <IntegerArgumentType name="U8" signed="false" sizeInBits="8"/>
                </ArgumentTypeSet>
                <MetaCommandSet>
                  <MetaCommand name="Reboot">
                    <ArgumentList>
                      <Argument name="delay" argumentTypeRef="U8">
                        <AliasSet><!-- ops names --><Alias nameSpace="ops" alias="RESTART_DELAY"/></AliasSet>
                      </Argument>
                    </ArgumentList>
                  </MetaCommand>
                </MetaCommandSet>
              </CommandMetaData>
            </SpaceSystem>
            """);
        var argument = Assert.Single(loaded.CommandMetaData!.MetaCommands.Single().Arguments!);

        Assert.Equal("RESTART_DELAY", Assert.Single(argument.Description!.Aliases!).Alias);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Contains("ops names", xml); // the comment survives the round trip
    }

    [Test]
    public void Load_CommentInsideUnitSet_SurvivesTheRoundTrip()
    {
        // Same bug class as the AliasSet case: the row readers used to swallow comments.
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <TelemetryMetaData>
                <ParameterTypeSet>
                  <IntegerParameterType name="Volt" signed="false" sizeInBits="16">
                    <UnitSet><!-- calibrated units --><Unit>mV</Unit></UnitSet>
                  </IntegerParameterType>
                </ParameterTypeSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);

        Assert.Equal("mV", Assert.Single(loaded.TelemetryMetaData!.ParameterTypeSet.Single().UnitSet!).Value);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Contains("calibrated units", xml);
    }
}
