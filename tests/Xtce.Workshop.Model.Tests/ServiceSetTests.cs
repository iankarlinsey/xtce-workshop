using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>Issue #115: ServiceSet modeled — names plus container/message refs.</summary>
public class ServiceSetTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [Test]
    public void Load_ModelsServices_AndRoundTripsThem()
    {
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <ServiceSet>
                <Service name="Housekeeping">
                  <MessageRefSet><MessageRef messageRef="HkMsg"/></MessageRefSet>
                </Service>
                <Service name="Dump">
                  <ContainerRefSet><ContainerRef containerRef="DumpFrame"/><ContainerRef containerRef="DumpTail"/></ContainerRefSet>
                </Service>
              </ServiceSet>
              <TelemetryMetaData>
                <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
                <ParameterSet><Parameter name="P" parameterTypeRef="T"/></ParameterSet>
                <ContainerSet>
                  <SequenceContainer name="DumpFrame"><EntryList/></SequenceContainer>
                  <SequenceContainer name="DumpTail"><EntryList/></SequenceContainer>
                </ContainerSet>
                <MessageSet>
                  <Message name="HkMsg"><MatchCriteria><Comparison parameterRef="P" value="1"/></MatchCriteria><ContainerRef containerRef="DumpFrame"/></Message>
                </MessageSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);

        Assert.Equal(["Housekeeping", "Dump"], loaded.ServiceSet!.Select(s => s.Name));
        Assert.Equal(["HkMsg"], loaded.ServiceSet[0].MessageRefs!);
        Assert.Equal(["DumpFrame", "DumpTail"], loaded.ServiceSet[1].ContainerRefs!);

        var written = XtceDocumentWriter.Write(loaded);
        Assert.Equal(loaded, Load(written));
        var errors = XsdValidation.Validate(written);
        Assert.True(errors.Count == 0, "Writer output failed XSD validation:\n" + string.Join("\n", errors));
    }
}
