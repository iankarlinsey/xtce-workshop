using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>Issue #114: StreamSet modeled shallowly on both metadata sides.</summary>
public class StreamSetTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string Sample => $"""
        <SpaceSystem xmlns="{Ns}" name="S">
          <TelemetryMetaData>
            <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
            <ParameterSet><Parameter name="P" parameterTypeRef="T"/></ParameterSet>
            <ContainerSet>
              <SequenceContainer name="Frame"><EntryList><ParameterRefEntry parameterRef="P"/></EntryList></SequenceContainer>
            </ContainerSet>
            <StreamSet>
              <FixedFrameStream name="Downlink" bitRateInBPS="9600" frameLengthInBits="8920" syncApertureInBits="0">
                <ContainerRef containerRef="Frame"/>
                <SyncStrategy><SyncPattern pattern="1ACFFC1D" patternLengthInBits="32"/></SyncStrategy>
              </FixedFrameStream>
            </StreamSet>
          </TelemetryMetaData>
        </SpaceSystem>
        """;

    [Test]
    public void Load_ModelsStreams_AndRoundTripsThem()
    {
        var loaded = Load(Sample);

        var stream = loaded.TelemetryMetaData!.StreamSet!.Single();
        Assert.Equal((StreamKind.FixedFrame, "Downlink", "Frame", "8920", "9600"),
            (stream.Kind, stream.Name, stream.ContainerRef, stream.FrameLengthInBits, stream.BitRateInBps));
        Assert.Equal(["SyncStrategy"], stream.Preserved!.Select(f => f.ElementName).ToList());
        Assert.Equal("syncApertureInBits", Assert.Single(stream.PreservedAttributes!).Name);

        var written = XtceDocumentWriter.Write(loaded);
        Assert.Equal(loaded, Load(written));
        var errors = XsdValidation.Validate(written);
        Assert.True(errors.Count == 0, "Writer output failed XSD validation:\n" + string.Join("\n", errors));
        // ContainerRef precedes StreamRef/SyncStrategy per FrameStreamType order.
        Assert.True(written.IndexOf("<ContainerRef", StringComparison.Ordinal)
                    < written.IndexOf("<SyncStrategy", StringComparison.Ordinal));
    }

    [Test]
    public void StreamOddShapes_FallBackLosslessly()
    {
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <TelemetryMetaData>
                <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
                <ParameterSet/>
                <StreamSet>
                  <VariableFrameStream name="VarLink">
                    <ServiceRef serviceRef="Svc"/>
                    <SyncStrategy><Flag flagSizeInBits="8" flagBitType="01"/></SyncStrategy>
                  </VariableFrameStream>
                  <SomethingNew/>
                </StreamSet>
              </TelemetryMetaData>
              <CommandMetaData>
                <StreamSet>
                  <CustomStream name="Uplink" bitRateInBPS="1200"><EncodingAlgorithm name="A"><AlgorithmText>x</AlgorithmText></EncodingAlgorithm><DecodingAlgorithm name="B"><AlgorithmText>y</AlgorithmText></DecodingAlgorithm></CustomStream>
                </StreamSet>
                <MetaCommandSet><MetaCommand name="Cmd"/></MetaCommandSet>
              </CommandMetaData>
            </SpaceSystem>
            """);

        var telemetryStreams = loaded.TelemetryMetaData!.StreamSet!;
        // ServiceRef content stays preserved (only ContainerRef is modeled).
        Assert.Null(telemetryStreams[0].ContainerRef);
        Assert.Equal(["ServiceRef", "SyncStrategy"], telemetryStreams[0].Preserved!.Select(f => f.ElementName).ToList());
        Assert.Equal(StreamKind.VariableFrame, telemetryStreams[0].Kind);
        // A foreign set entry rides whole.
        Assert.Equal("SomethingNew", telemetryStreams[1].RawXml!.ElementName);

        var commandStream = loaded.CommandMetaData!.StreamSet!.Single();
        Assert.Equal((StreamKind.Custom, "Uplink", "1200"),
            (commandStream.Kind, commandStream.Name, commandStream.BitRateInBps));

        var written = XtceDocumentWriter.Write(loaded);
        Assert.Equal(loaded, Load(written));
    }
}
