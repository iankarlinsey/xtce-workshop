using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Issues #100/#101/#102: the time types' Encoding wrapper, ParameterProperties, and
/// UnitSet are modeled — attributes first-class, deep children preserved, lossless.
/// </summary>
public class UnitsPropertiesTimeEncodingTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string Sample => $"""
        <SpaceSystem xmlns="{Ns}" name="S">
          <TelemetryMetaData>
            <ParameterTypeSet>
              <FloatParameterType name="Power_Type">
                <UnitSet>
                  <Unit power="2" factor="1000" description="kilo" form="calibrated">W</Unit>
                  <Unit>V</Unit>
                </UnitSet>
                <FloatDataEncoding sizeInBits="32"/>
              </FloatParameterType>
              <AbsoluteTimeParameterType name="Mission_Type">
                <Encoding units="seconds" scale="0.001" offset="1.5">
                  <IntegerDataEncoding sizeInBits="64" encoding="unsigned"/>
                </Encoding>
                <ReferenceTime><Epoch>TAI</Epoch></ReferenceTime>
              </AbsoluteTimeParameterType>
            </ParameterTypeSet>
            <ParameterSet>
              <Parameter name="Power" parameterTypeRef="Power_Type">
                <ParameterProperties dataSource="constant" readOnly="false" persistence="false">
                  <SystemName>eps</SystemName>
                </ParameterProperties>
              </Parameter>
            </ParameterSet>
          </TelemetryMetaData>
        </SpaceSystem>
        """;

    [Test]
    public void Load_ModelsUnits_Properties_AndTimeEncoding()
    {
        var document = Load(Sample);
        var types = document.TelemetryMetaData!.ParameterTypeSet;

        var power = types.Single(t => t.Name == "Power_Type");
        Assert.Equal(2, power.UnitSet!.Count);
        Assert.Equal(("W", "kilo", "2", "1000", "calibrated"),
            (power.UnitSet[0].Value, power.UnitSet[0].Description, power.UnitSet[0].Power,
             power.UnitSet[0].Factor, power.UnitSet[0].Form));
        Assert.Equal(("V", null), (power.UnitSet[1].Value, power.UnitSet[1].Description));

        var mission = types.Single(t => t.Name == "Mission_Type");
        var timeEncoding = mission.TimeEncoding!;
        Assert.Equal(("seconds", "0.001", "1.5"), (timeEncoding.Units, timeEncoding.Scale, timeEncoding.Offset));
        Assert.Equal((DataEncodingKind.Integer, 64L), (timeEncoding.DataEncoding!.Kind, timeEncoding.DataEncoding.SizeInBits));
        // ReferenceTime stays a preserved fragment on the type.
        Assert.Equal(["ReferenceTime"], mission.Preserved!.Select(f => f.ElementName).ToList());

        var parameter = document.TelemetryMetaData.ParameterSet.Single();
        var properties = parameter.Properties!;
        Assert.Equal(("constant", false, false), (properties.DataSource, properties.ReadOnly, properties.Persistence));
        Assert.Equal(["SystemName"], properties.Preserved!.Select(f => f.ElementName).ToList());
    }

    [Test]
    public void RoundTrip_IsLosslessAndSchemaValid()
    {
        var loaded = Load(Sample);

        var xml = XtceDocumentWriter.Write(loaded);
        var reloaded = Load(xml);

        Assert.Equal(loaded, reloaded);
        var errors = XsdValidation.Validate(xml);
        Assert.True(errors.Count == 0, "Writer output failed XSD validation:\n" + string.Join("\n", errors));
        // BaseTimeDataType order: Encoding before ReferenceTime; BaseDataType: UnitSet before the encoding.
        Assert.True(xml.IndexOf("<Encoding", StringComparison.Ordinal) < xml.IndexOf("<ReferenceTime", StringComparison.Ordinal));
        Assert.True(xml.IndexOf("<UnitSet", StringComparison.Ordinal) < xml.IndexOf("<FloatDataEncoding", StringComparison.Ordinal));
        Assert.Contains(">W</Unit>", xml);
    }

    [Test]
    public void Load_TimeEncoding_OnArgumentTypes_UsesTheTypoElementAroundIt()
    {
        // The wrapper works on argument types too (the outer element is the XSD's
        // RelativeTimeAgumentType typo; the Encoding wrapper inside is shared).
        var document = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <CommandMetaData>
                <ArgumentTypeSet>
                  <RelativeTimeAgumentType name="Delay_Type">
                    <Encoding units="seconds"><IntegerDataEncoding sizeInBits="16"/></Encoding>
                  </RelativeTimeAgumentType>
                </ArgumentTypeSet>
              </CommandMetaData>
            </SpaceSystem>
            """);

        var type = document.CommandMetaData!.ArgumentTypeSet!.Single();
        Assert.Equal(16, type.TimeEncoding!.DataEncoding!.SizeInBits);

        var written = XtceDocumentWriter.Write(document);
        Assert.Equal(document, Load(written));
        Assert.Contains("<RelativeTimeAgumentType", written);
    }

    [Test]
    public void Load_ModelsNumericDefaultAlarms_AndBailsOutOnOddShapes()
    {
        var xml = $"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <TelemetryMetaData>
                <ParameterTypeSet>
                  <IntegerParameterType name="A">
                    <IntegerDataEncoding sizeInBits="8"/>
                    <DefaultAlarm minViolations="3">
                      <StaticAlarmRanges rangeForm="outside">
                        <WarningRange minInclusive="10" maxInclusive="90"/>
                        <CriticalRange minExclusive="0" maxExclusive="100"/>
                      </StaticAlarmRanges>
                    </DefaultAlarm>
                  </IntegerParameterType>
                  <FloatParameterType name="B">
                    <FloatDataEncoding/>
                    <DefaultAlarm>
                      <StaticAlarmRanges>
                        <!-- comment forces the bail-out -->
                        <WarningRange minInclusive="1"/>
                      </StaticAlarmRanges>
                    </DefaultAlarm>
                  </FloatParameterType>
                </ParameterTypeSet>
                <ParameterSet/>
              </TelemetryMetaData>
            </SpaceSystem>
            """;
        var loaded = Load(xml);
        var types = loaded.TelemetryMetaData!.ParameterTypeSet;

        var alarm = types.Single(t => t.Name == "A").DefaultAlarm!;
        Assert.Equal((3L, "outside", true), (alarm.MinViolations!.Value, alarm.RangeForm, alarm.HasStaticRanges));
        Assert.Equal(("10", "90"), (alarm.WarningRange!.MinInclusive, alarm.WarningRange.MaxInclusive));
        Assert.Equal(("0", "100"), (alarm.CriticalRange!.MinExclusive, alarm.CriticalRange.MaxExclusive));
        Assert.Null(alarm.WatchRange);

        // The commented ranges stay a preserved fragment on the alarm — nothing modeled.
        var bailed = types.Single(t => t.Name == "B").DefaultAlarm!;
        Assert.False(bailed.HasStaticRanges);
        Assert.Equal(["StaticAlarmRanges"], bailed.Preserved!.Select(f => f.ElementName).ToList());

        var written = XtceDocumentWriter.Write(loaded);
        Assert.Equal(loaded, Load(written));
        var errors = XsdValidation.Validate(written);
        Assert.True(errors.Count == 0, "Writer output failed XSD validation:\n" + string.Join("\n", errors));
    }
}
