using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Modeled non-numeric DefaultAlarms (#119): EnumerationAlarmType, StringAlarmType, and
/// the bare AlarmType shape of Boolean/Binary — with AlarmConditions as per-level
/// MatchCriteria.
/// </summary>
public class NonNumericAlarmTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string Wrap(string typeSetXml) => $"""
        <SpaceSystem xmlns="{Ns}" name="S">
          <TelemetryMetaData>
            <ParameterTypeSet>
              {typeSetXml}
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
    public void Load_ModelsEnumerationAlarm()
    {
        var loaded = Load(Wrap("""
            <EnumeratedParameterType name="Status">
              <UnitSet/>
              <EnumerationList>
                <Enumeration value="0" label="OK"/>
                <Enumeration value="1" label="DEGRADED"/>
                <Enumeration value="2" label="FAILED"/>
              </EnumerationList>
              <DefaultAlarm minViolations="2" defaultAlarmLevel="watch">
                <EnumerationAlarmList>
                  <EnumerationAlarm alarmLevel="warning" enumerationLabel="DEGRADED"/>
                  <EnumerationAlarm alarmLevel="critical" enumerationLabel="FAILED"/>
                </EnumerationAlarmList>
              </DefaultAlarm>
            </EnumeratedParameterType>
            """));
        var alarm = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Status")
            .NonNumericDefaultAlarm!;

        Assert.Equal(2, alarm.MinViolations);
        Assert.Equal("watch", alarm.DefaultAlarmLevel);
        Assert.Equal(2, alarm.EnumerationAlarms!.Count);
        Assert.Equal(("warning", "DEGRADED"),
            (alarm.EnumerationAlarms[0].AlarmLevel, alarm.EnumerationAlarms[0].EnumerationLabel));
        Assert.Equal(("critical", "FAILED"),
            (alarm.EnumerationAlarms[1].AlarmLevel, alarm.EnumerationAlarms[1].EnumerationLabel));
        Assert.Null(alarm.Preserved);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_ModelsStringAlarm()
    {
        var loaded = Load(Wrap("""
            <StringParameterType name="Msg">
              <UnitSet/>
              <DefaultAlarm defaultAlarmLevel="normal">
                <StringAlarmList>
                  <StringAlarm alarmLevel="warning" matchPattern="WARN.*"/>
                  <StringAlarm alarmLevel="critical" matchPattern="ERR.*"/>
                </StringAlarmList>
              </DefaultAlarm>
            </StringParameterType>
            """));
        var alarm = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Msg")
            .NonNumericDefaultAlarm!;

        Assert.Equal("normal", alarm.DefaultAlarmLevel);
        Assert.Equal("WARN.*", alarm.StringAlarms![0].MatchPattern);
        Assert.Equal("critical", alarm.StringAlarms[1].AlarmLevel);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_ModelsBooleanAlarmConditions_PerLevelMatchCriteria()
    {
        var loaded = Load(Wrap("""
            <BooleanParameterType name="HeaterOn">
              <UnitSet/>
              <DefaultAlarm minViolations="5">
                <AlarmConditions>
                  <WarningAlarm>
                    <Comparison parameterRef="Mode" value="1"/>
                  </WarningAlarm>
                  <CriticalAlarm>
                    <ComparisonList>
                      <Comparison parameterRef="Mode" value="2"/>
                      <Comparison parameterRef="Mode" comparisonOperator="&gt;=" value="4"/>
                    </ComparisonList>
                  </CriticalAlarm>
                </AlarmConditions>
              </DefaultAlarm>
            </BooleanParameterType>
            """));
        var alarm = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "HeaterOn")
            .NonNumericDefaultAlarm!;

        Assert.Equal(5, alarm.MinViolations);
        Assert.Null(alarm.DefaultAlarmLevel); // not a BooleanAlarmType attribute
        Assert.Equal("Mode", alarm.Conditions!.Warning!.Comparison!.ParameterRef);
        Assert.Equal(2, alarm.Conditions.Critical!.ComparisonList!.Count);
        Assert.Null(alarm.Conditions.Watch);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_BinaryAlarmWithCustomAlarm_KeepsItPreserved()
    {
        var loaded = Load(Wrap("""
            <BinaryParameterType name="Blob">
              <UnitSet/>
              <DefaultAlarm>
                <CustomAlarm>
                  <InputAlgorithm name="check">
                    <AlgorithmText language="python">return False</AlgorithmText>
                  </InputAlgorithm>
                </CustomAlarm>
              </DefaultAlarm>
            </BinaryParameterType>
            """));
        var alarm = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Blob")
            .NonNumericDefaultAlarm!;

        Assert.Equal("CustomAlarm", Assert.Single(alarm.Preserved!).ElementName);
        Assert.Null(alarm.Conditions);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_CommentInsideEnumerationAlarmList_PreservesTheList()
    {
        var loaded = Load(Wrap("""
            <EnumeratedParameterType name="Status">
              <UnitSet/>
              <EnumerationList>
                <Enumeration value="0" label="OK"/>
              </EnumerationList>
              <DefaultAlarm>
                <EnumerationAlarmList>
                  <!-- failure states -->
                  <EnumerationAlarm alarmLevel="critical" enumerationLabel="FAILED"/>
                </EnumerationAlarmList>
              </DefaultAlarm>
            </EnumeratedParameterType>
            """));
        var alarm = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Status")
            .NonNumericDefaultAlarm!;

        Assert.Null(alarm.EnumerationAlarms);
        Assert.Equal("EnumerationAlarmList", Assert.Single(alarm.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_AlarmConditionsWithBooleanExpressionLevel_StaysModeledWithPreservedCriteria()
    {
        var loaded = Load(Wrap("""
            <BooleanParameterType name="HeaterOn">
              <UnitSet/>
              <DefaultAlarm>
                <AlarmConditions>
                  <WarningAlarm>
                    <BooleanExpression>
                      <Condition><ParameterInstanceRef parameterRef="Mode"/><ComparisonOperator>==</ComparisonOperator><Value>1</Value></Condition>
                    </BooleanExpression>
                  </WarningAlarm>
                </AlarmConditions>
              </DefaultAlarm>
            </BooleanParameterType>
            """));
        var conditions = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "HeaterOn")
            .NonNumericDefaultAlarm!.Conditions!;

        Assert.Null(conditions.Warning!.Comparison);
        // Modeled as a tree since #124 — no fragment left behind.
        Assert.Null(conditions.Warning.Preserved);
        Assert.Equal("1", conditions.Warning.BooleanExpression!.Value);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void RoundTrip_NumericDefaultAlarm_IsUntouchedByTheNonNumericPath()
    {
        // Guard: the numeric model still claims Integer/Float DefaultAlarms.
        var loaded = Load(Wrap("""
            <IntegerParameterType name="Volt" signed="false" sizeInBits="16">
              <UnitSet/>
              <DefaultAlarm>
                <StaticAlarmRanges><WarningRange minInclusive="1"/></StaticAlarmRanges>
              </DefaultAlarm>
            </IntegerParameterType>
            """));
        var type = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Volt");

        Assert.NotNull(type.DefaultAlarm);
        Assert.Null(type.NonNumericDefaultAlarm);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }
}
