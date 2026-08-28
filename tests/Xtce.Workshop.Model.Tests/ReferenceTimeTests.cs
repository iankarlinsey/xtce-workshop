using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Modeled ReferenceTime on the time types (#122): Epoch text verbatim, or an OffsetFrom
/// parameter-instance ref.
/// </summary>
public class ReferenceTimeTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string Wrap(string typeSetXml) => $"""
        <SpaceSystem xmlns="{Ns}" name="S">
          <TelemetryMetaData>
            <ParameterTypeSet>
              {typeSetXml}
            </ParameterTypeSet>
            <ParameterSet>
              <Parameter name="Seconds" parameterTypeRef="Uptime"/>
            </ParameterSet>
          </TelemetryMetaData>
        </SpaceSystem>
        """;

    private const string TimeEncodingXml = """
        <Encoding units="seconds">
          <IntegerDataEncoding sizeInBits="32"/>
        </Encoding>
        """;

    private static string RoundTrip(SpaceSystem loaded, out SpaceSystem reloaded)
    {
        var xml = XtceDocumentWriter.Write(loaded);
        reloaded = Load(xml);
        return xml;
    }

    [Test]
    public void Load_ModelsEpochReference()
    {
        var loaded = Load(Wrap($"""
            <AbsoluteTimeParameterType name="MissionTime">
              {TimeEncodingXml}
              <ReferenceTime>
                <Epoch>TAI</Epoch>
              </ReferenceTime>
            </AbsoluteTimeParameterType>
            <RelativeTimeParameterType name="Uptime">
              {TimeEncodingXml}
            </RelativeTimeParameterType>
            """));
        var type = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "MissionTime");

        Assert.Equal("TAI", type.ReferenceTime!.Epoch);
        Assert.Null(type.ReferenceTime.OffsetFromParameterRef);
        Assert.Null(type.Preserved);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_ModelsDateTimeEpochVerbatim()
    {
        var loaded = Load(Wrap($"""
            <AbsoluteTimeParameterType name="MissionTime">
              {TimeEncodingXml}
              <ReferenceTime>
                <Epoch>1980-01-06T00:00:00</Epoch>
              </ReferenceTime>
            </AbsoluteTimeParameterType>
            <RelativeTimeParameterType name="Uptime">
              {TimeEncodingXml}
            </RelativeTimeParameterType>
            """));

        Assert.Equal("1980-01-06T00:00:00",
            loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "MissionTime").ReferenceTime!.Epoch);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_ModelsOffsetFromReference()
    {
        var loaded = Load(Wrap($"""
            <AbsoluteTimeParameterType name="MissionTime">
              {TimeEncodingXml}
              <ReferenceTime>
                <OffsetFrom parameterRef="Seconds" instance="-1" useCalibratedValue="false"/>
              </ReferenceTime>
            </AbsoluteTimeParameterType>
            <RelativeTimeParameterType name="Uptime">
              {TimeEncodingXml}
            </RelativeTimeParameterType>
            """));
        var referenceTime = loaded.TelemetryMetaData!.ParameterTypeSet
            .Single(t => t.Name == "MissionTime").ReferenceTime!;

        Assert.Equal("Seconds", referenceTime.OffsetFromParameterRef);
        Assert.Equal(-1, referenceTime.OffsetFromInstance);
        Assert.Equal(false, referenceTime.OffsetFromUseCalibratedValue);
        Assert.Null(referenceTime.Epoch);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_OffsetFromAbsentAttributes_StayNull()
    {
        var loaded = Load(Wrap($"""
            <RelativeTimeParameterType name="Uptime">
              {TimeEncodingXml}
              <ReferenceTime>
                <OffsetFrom parameterRef="Seconds"/>
              </ReferenceTime>
            </RelativeTimeParameterType>
            """));
        var referenceTime = loaded.TelemetryMetaData!.ParameterTypeSet.Single().ReferenceTime!;

        Assert.Null(referenceTime.OffsetFromInstance); // XSD default 0 never baked in
        Assert.Null(referenceTime.OffsetFromUseCalibratedValue); // XSD default true never baked in

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_CommentInsideReferenceTime_PreservesTheWholeElement()
    {
        var loaded = Load(Wrap($"""
            <RelativeTimeParameterType name="Uptime">
              {TimeEncodingXml}
              <ReferenceTime>
                <!-- GPS week epoch -->
                <Epoch>GPS</Epoch>
              </ReferenceTime>
            </RelativeTimeParameterType>
            """));
        var type = loaded.TelemetryMetaData!.ParameterTypeSet.Single();

        Assert.Null(type.ReferenceTime);
        Assert.Equal("ReferenceTime", Assert.Single(type.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_OffsetFromWithChildContent_PreservesTheWholeElement()
    {
        // A non-empty OffsetFrom is schema-invalid but must stay lossless.
        var loaded = Load(Wrap($"""
            <RelativeTimeParameterType name="Uptime">
              {TimeEncodingXml}
              <ReferenceTime>
                <OffsetFrom parameterRef="Seconds"><Mystery/></OffsetFrom>
              </ReferenceTime>
            </RelativeTimeParameterType>
            """));
        var type = loaded.TelemetryMetaData!.ParameterTypeSet.Single();

        Assert.Null(type.ReferenceTime);
        Assert.Equal("ReferenceTime", Assert.Single(type.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_ReferenceTimeOnNonTimeType_StaysPreserved()
    {
        var loaded = Load(Wrap("""
            <IntegerParameterType name="Uptime" signed="false" sizeInBits="16">
              <UnitSet/>
              <ReferenceTime><Epoch>TAI</Epoch></ReferenceTime>
            </IntegerParameterType>
            """));
        var type = loaded.TelemetryMetaData!.ParameterTypeSet.Single();

        Assert.Null(type.ReferenceTime);
        Assert.Equal("ReferenceTime", Assert.Single(type.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }
}
