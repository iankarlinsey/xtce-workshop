using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Modeled non-numeric context-alarm lists (#120): NonNumericAlarm bodies plus their
/// ContextMatch, on Enumerated/Boolean/String (ContextAlarmList) and Binary
/// (BinaryContextAlarmList).
/// </summary>
public class NonNumericContextAlarmTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string Wrap(string typeXml) => $"""
        <SpaceSystem xmlns="{Ns}" name="S">
          <TelemetryMetaData>
            <ParameterTypeSet>
              {typeXml}
              <IntegerParameterType name="ModeType" signed="false" sizeInBits="8">
                <UnitSet/>
              </IntegerParameterType>
            </ParameterTypeSet>
            <ParameterSet>
              <Parameter name="Mode" parameterTypeRef="ModeType"/>
            </ParameterSet>
          </TelemetryMetaData>
        </SpaceSystem>
        """;

    private static string RoundTrip(SpaceSystem loaded, out SpaceSystem reloaded)
    {
        var xml = XtceDocumentWriter.Write(loaded);
        reloaded = Load(xml);
        return xml;
    }

    [Test]
    public void Load_ModelsEnumerationContextAlarms_InListOrder()
    {
        var loaded = Load(Wrap("""
            <EnumeratedParameterType name="Status">
              <UnitSet/>
              <EnumerationList>
                <Enumeration value="0" label="OK"/>
                <Enumeration value="2" label="FAILED"/>
              </EnumerationList>
              <DefaultAlarm>
                <EnumerationAlarmList>
                  <EnumerationAlarm alarmLevel="critical" enumerationLabel="FAILED"/>
                </EnumerationAlarmList>
              </DefaultAlarm>
              <ContextAlarmList>
                <ContextAlarm defaultAlarmLevel="watch">
                  <EnumerationAlarmList>
                    <EnumerationAlarm alarmLevel="warning" enumerationLabel="FAILED"/>
                  </EnumerationAlarmList>
                  <ContextMatch><Comparison parameterRef="Mode" value="1"/></ContextMatch>
                </ContextAlarm>
              </ContextAlarmList>
            </EnumeratedParameterType>
            """));
        var type = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Status");

        Assert.NotNull(type.NonNumericDefaultAlarm);
        var entry = Assert.Single(type.NonNumericContextAlarms!);
        Assert.Equal("watch", entry.Alarm!.DefaultAlarmLevel);
        Assert.Equal(("warning", "FAILED"),
            (entry.Alarm.EnumerationAlarms![0].AlarmLevel, entry.Alarm.EnumerationAlarms[0].EnumerationLabel));
        Assert.Equal("Mode", entry.Context!.Comparison!.ParameterRef);
        Assert.Null(entry.Alarm.Preserved); // ContextMatch modeled, not preserved

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_BinaryContextAlarmList_UsesItsOwnElementName()
    {
        var loaded = Load(Wrap("""
            <BinaryParameterType name="Blob">
              <UnitSet/>
              <BinaryContextAlarmList>
                <ContextAlarm minViolations="2">
                  <AlarmConditions>
                    <WarningAlarm><Comparison parameterRef="Mode" value="1"/></WarningAlarm>
                  </AlarmConditions>
                  <ContextMatch><Comparison parameterRef="Mode" value="1"/></ContextMatch>
                </ContextAlarm>
              </BinaryContextAlarmList>
            </BinaryParameterType>
            """));
        var entry = Assert.Single(loaded.TelemetryMetaData!.ParameterTypeSet
            .Single(t => t.Name == "Blob").NonNumericContextAlarms!);

        Assert.Equal(2, entry.Alarm!.MinViolations);
        Assert.Equal("Mode", entry.Alarm.Conditions!.Warning!.Comparison!.ParameterRef);
        Assert.NotNull(entry.Context);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Contains("<BinaryContextAlarmList>", xml);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_StringAndBooleanContextAlarms_RoundTripSchemaValid()
    {
        var loaded = Load(Wrap("""
            <StringParameterType name="Msg">
              <UnitSet/>
              <ContextAlarmList>
                <ContextAlarm defaultAlarmLevel="normal">
                  <StringAlarmList>
                    <StringAlarm alarmLevel="critical" matchPattern="FATAL.*"/>
                  </StringAlarmList>
                  <ContextMatch><Comparison parameterRef="Mode" value="2"/></ContextMatch>
                </ContextAlarm>
              </ContextAlarmList>
            </StringParameterType>
            <BooleanParameterType name="HeaterOn">
              <UnitSet/>
              <ContextAlarmList>
                <ContextAlarm>
                  <AlarmConditions>
                    <CriticalAlarm><Comparison parameterRef="Mode" value="3"/></CriticalAlarm>
                  </AlarmConditions>
                  <ContextMatch><Comparison parameterRef="Mode" value="1"/></ContextMatch>
                </ContextAlarm>
              </ContextAlarmList>
            </BooleanParameterType>
            """));

        var msg = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Msg");
        Assert.Equal("FATAL.*", Assert.Single(msg.NonNumericContextAlarms!).Alarm!.StringAlarms![0].MatchPattern);
        var heater = loaded.TelemetryMetaData.ParameterTypeSet.Single(t => t.Name == "HeaterOn");
        Assert.NotNull(Assert.Single(heater.NonNumericContextAlarms!).Alarm!.Conditions!.Critical);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_CommentBetweenEntries_PreservesTheWholeList()
    {
        var loaded = Load(Wrap("""
            <BooleanParameterType name="HeaterOn">
              <UnitSet/>
              <ContextAlarmList>
                <!-- safe-mode alarm -->
                <ContextAlarm>
                  <ContextMatch><Comparison parameterRef="Mode" value="1"/></ContextMatch>
                </ContextAlarm>
              </ContextAlarmList>
            </BooleanParameterType>
            """));
        var type = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "HeaterOn");

        Assert.Null(type.NonNumericContextAlarms);
        Assert.Equal("ContextAlarmList", Assert.Single(type.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_NumericContextAlarmList_IsNotClaimedByTheNonNumericModel()
    {
        var loaded = Load(Wrap("""
            <IntegerParameterType name="Volt" signed="false" sizeInBits="16">
              <UnitSet/>
              <ContextAlarmList>
                <ContextAlarm>
                  <StaticAlarmRanges><WarningRange minInclusive="1"/></StaticAlarmRanges>
                  <ContextMatch><Comparison parameterRef="Mode" value="1"/></ContextMatch>
                </ContextAlarm>
              </ContextAlarmList>
            </IntegerParameterType>
            """));
        var type = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Volt");

        Assert.NotNull(type.ContextAlarms);
        Assert.Null(type.NonNumericContextAlarms);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }
}
