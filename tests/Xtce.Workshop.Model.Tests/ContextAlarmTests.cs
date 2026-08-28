using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Modeled numeric ContextAlarmList entries (#118): a full NumericAlarm body plus its
/// ContextMatch, kept in list order (first matching context wins).
/// </summary>
public class ContextAlarmTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string WrapTypeChildren(string children) => $"""
        <SpaceSystem xmlns="{Ns}" name="S">
          <TelemetryMetaData>
            <ParameterTypeSet>
              <FloatParameterType name="Volt">
                <UnitSet/>
                {children}
              </FloatParameterType>
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

    private const string DefaultPlusContexts = """
        <DefaultAlarm>
          <StaticAlarmRanges>
            <WarningRange minInclusive="3.0" maxInclusive="16.0"/>
          </StaticAlarmRanges>
        </DefaultAlarm>
        <ContextAlarmList>
          <ContextAlarm minViolations="3">
            <StaticAlarmRanges rangeForm="outside">
              <WarningRange minInclusive="6.5" maxInclusive="16.0"/>
              <CriticalRange minInclusive="5.0" maxInclusive="17.0"/>
            </StaticAlarmRanges>
            <ContextMatch>
              <Comparison parameterRef="Mode" value="1"/>
            </ContextMatch>
          </ContextAlarm>
          <ContextAlarm>
            <StaticAlarmRanges>
              <WarningRange minInclusive="0.5"/>
            </StaticAlarmRanges>
            <ContextMatch>
              <ComparisonList>
                <Comparison parameterRef="Mode" value="2"/>
                <Comparison parameterRef="Mode" comparisonOperator="&lt;=" value="5"/>
              </ComparisonList>
            </ContextMatch>
          </ContextAlarm>
        </ContextAlarmList>
        """;

    [Test]
    public void Load_ModelsContextAlarms_InListOrder()
    {
        var type = Load(WrapTypeChildren(DefaultPlusContexts))
            .TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Volt");

        Assert.NotNull(type.DefaultAlarm);
        Assert.Null(type.Preserved);
        Assert.Equal(2, type.ContextAlarms!.Count);

        var first = type.ContextAlarms[0];
        Assert.Equal(3, first.Alarm!.MinViolations);
        Assert.Equal("outside", first.Alarm.RangeForm);
        Assert.Equal("6.5", first.Alarm.WarningRange!.MinInclusive);
        Assert.Equal("17.0", first.Alarm.CriticalRange!.MaxInclusive);
        Assert.Equal("Mode", first.Context!.Comparison!.ParameterRef);
        Assert.Null(first.Alarm.Preserved); // ContextMatch is modeled, not preserved

        var second = type.ContextAlarms[1];
        Assert.Null(second.Alarm!.MinViolations);
        Assert.Equal(2, second.Context!.ComparisonList!.Count);
    }

    [Test]
    public void RoundTrip_ContextAlarms_IsLosslessAndSchemaValid()
    {
        var loaded = Load(WrapTypeChildren(DefaultPlusContexts));

        var xml = RoundTrip(loaded, out var reloaded);

        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_UnmodelableAlarmBody_StaysPreservedInsideTheEntry()
    {
        var loaded = Load(WrapTypeChildren("""
            <ContextAlarmList>
              <ContextAlarm>
                <ChangeAlarmRanges changeType="changePerSecond">
                  <WarningRange minInclusive="1"/>
                </ChangeAlarmRanges>
                <ContextMatch><Comparison parameterRef="Mode" value="1"/></ContextMatch>
              </ContextAlarm>
            </ContextAlarmList>
            """));
        var entry = Assert.Single(loaded.TelemetryMetaData!.ParameterTypeSet
            .Single(t => t.Name == "Volt").ContextAlarms!);

        Assert.Equal("ChangeAlarmRanges", Assert.Single(entry.Alarm!.Preserved!).ElementName);
        Assert.Equal("Mode", entry.Context!.Comparison!.ParameterRef);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_CommentBetweenEntries_PreservesTheWholeList()
    {
        var loaded = Load(WrapTypeChildren("""
            <ContextAlarmList>
              <!-- safe-mode thresholds -->
              <ContextAlarm>
                <StaticAlarmRanges><WarningRange minInclusive="1"/></StaticAlarmRanges>
                <ContextMatch><Comparison parameterRef="Mode" value="1"/></ContextMatch>
              </ContextAlarm>
            </ContextAlarmList>
            """));
        var type = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Volt");

        Assert.Null(type.ContextAlarms);
        Assert.Equal("ContextAlarmList", Assert.Single(type.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_ContextAlarmListOnNonNumericType_IsNotClaimedByTheNumericModel()
    {
        // Shares the element name but not the shape — since #120 it lands in the
        // non-numeric context list, never in the numeric one.
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <TelemetryMetaData>
                <ParameterTypeSet>
                  <EnumeratedParameterType name="Status">
                    <UnitSet/>
                    <EnumerationList>
                      <Enumeration value="0" label="OK"/>
                    </EnumerationList>
                    <ContextAlarmList>
                      <ContextAlarm defaultAlarmLevel="warning">
                        <ContextMatch><Comparison parameterRef="Mode" value="1"/></ContextMatch>
                      </ContextAlarm>
                    </ContextAlarmList>
                  </EnumeratedParameterType>
                </ParameterTypeSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);
        var type = loaded.TelemetryMetaData!.ParameterTypeSet.Single();

        Assert.Null(type.ContextAlarms);
        Assert.NotNull(type.NonNumericContextAlarms);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void RoundTrip_ProgrammaticRawEntry_IsEmittedInPosition()
    {
        var type = new ParameterTypeDefinition("Volt", ParameterTypeKind.Float,
            ContextAlarms:
            [
                new ContextNumericAlarm(
                    new NumericAlarm(WarningRange: new AlarmRange(MinInclusive: "1"), HasStaticRanges: true),
                    new MatchCriteria(new Comparison("Mode", "1"))),
                new ContextNumericAlarm(RawXml: new RawXmlFragment("ContextAlarm",
                    $"""<ContextAlarm xmlns="{Ns}"><AlarmConditions><WarningAlarm><Comparison parameterRef="Mode" value="9"/></WarningAlarm></AlarmConditions><ContextMatch><Comparison parameterRef="Mode" value="2"/></ContextMatch></ContextAlarm>""")),
            ]);
        var original = new SpaceSystem("S", [], new TelemetryMetaData([type], []));

        var xml = XtceDocumentWriter.Write(original);
        var reloaded = Load(xml);

        // The raw entry reloads as a modeled entry (AlarmConditions preserved inside),
        // proving position survived; the document itself must stay schema-valid.
        Assert.Equal(2, reloaded.TelemetryMetaData!.ParameterTypeSet.Single().ContextAlarms!.Count);
        var reloadedSecond = reloaded.TelemetryMetaData.ParameterTypeSet.Single().ContextAlarms![1];
        Assert.Equal("2", reloadedSecond.Context!.Comparison!.Value);
        Assert.Empty(XsdValidation.Validate(xml));
    }
}
