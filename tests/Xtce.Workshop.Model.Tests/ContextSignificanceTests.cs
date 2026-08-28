using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Modeled ContextSignificanceList entries (#121): ContextMatch + Significance, kept in
/// list order (first matching context overrides DefaultSignificance).
/// </summary>
public class ContextSignificanceTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string WrapCommandChildren(string children) => $"""
        <SpaceSystem xmlns="{Ns}" name="S">
          <TelemetryMetaData>
            <ParameterTypeSet>
              <IntegerParameterType name="ModeType" signed="false" sizeInBits="8">
                <UnitSet/>
              </IntegerParameterType>
            </ParameterTypeSet>
            <ParameterSet>
              <Parameter name="Mode" parameterTypeRef="ModeType"/>
            </ParameterSet>
          </TelemetryMetaData>
          <CommandMetaData>
            <MetaCommandSet>
              <MetaCommand name="Thrust">
                {children}
              </MetaCommand>
            </MetaCommandSet>
          </CommandMetaData>
        </SpaceSystem>
        """;

    private static string RoundTrip(SpaceSystem loaded, out SpaceSystem reloaded)
    {
        var xml = XtceDocumentWriter.Write(loaded);
        reloaded = Load(xml);
        return xml;
    }

    [Test]
    public void Load_ModelsContextSignificances_InListOrder()
    {
        var loaded = Load(WrapCommandChildren("""
            <DefaultSignificance consequenceLevel="normal"/>
            <ContextSignificanceList>
              <ContextSignificance>
                <ContextMatch><Comparison parameterRef="Mode" value="1"/></ContextMatch>
                <Significance consequenceLevel="critical" reasonForWarning="thruster fire in safe mode"/>
              </ContextSignificance>
              <ContextSignificance>
                <ContextMatch>
                  <ComparisonList>
                    <Comparison parameterRef="Mode" value="2"/>
                    <Comparison parameterRef="Mode" comparisonOperator="&lt;=" value="5"/>
                  </ComparisonList>
                </ContextMatch>
                <Significance consequenceLevel="vital"/>
              </ContextSignificance>
            </ContextSignificanceList>
            """));
        var metaCommand = loaded.CommandMetaData!.MetaCommands.Single();

        Assert.Equal("normal", metaCommand.DefaultSignificance!.ConsequenceLevel);
        Assert.Equal(2, metaCommand.ContextSignificances!.Count);
        var first = metaCommand.ContextSignificances[0];
        Assert.Equal("Mode", first.Context!.Comparison!.ParameterRef);
        Assert.Equal("critical", first.Significance!.ConsequenceLevel);
        Assert.Equal("thruster fire in safe mode", first.Significance.ReasonForWarning);
        var second = metaCommand.ContextSignificances[1];
        Assert.Equal(2, second.Context!.ComparisonList!.Count);
        Assert.Equal("vital", second.Significance!.ConsequenceLevel);
        Assert.Null(metaCommand.Preserved);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_SignificanceWithChildContent_RidesRawInPosition()
    {
        var loaded = Load(WrapCommandChildren("""
            <ContextSignificanceList>
              <ContextSignificance>
                <ContextMatch><Comparison parameterRef="Mode" value="1"/></ContextMatch>
                <Significance consequenceLevel="critical"/>
              </ContextSignificance>
              <ContextSignificance>
                <ContextMatch><Comparison parameterRef="Mode" value="2"/></ContextMatch>
                <Significance consequenceLevel="severe"><AncillaryDataSet><AncillaryData name="k">v</AncillaryData></AncillaryDataSet></Significance>
              </ContextSignificance>
            </ContextSignificanceList>
            """));
        var significances = loaded.CommandMetaData!.MetaCommands.Single().ContextSignificances!;

        Assert.Null(significances[0].RawXml);
        Assert.Equal("ContextSignificance", significances[1].RawXml!.ElementName);
        Assert.Null(significances[1].Significance);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_EntryMissingItsSignificanceHalf_RidesRaw()
    {
        var loaded = Load(WrapCommandChildren("""
            <ContextSignificanceList>
              <ContextSignificance>
                <ContextMatch><Comparison parameterRef="Mode" value="1"/></ContextMatch>
              </ContextSignificance>
            </ContextSignificanceList>
            """));
        var entry = Assert.Single(loaded.CommandMetaData!.MetaCommands.Single().ContextSignificances!);

        Assert.Equal("ContextSignificance", entry.RawXml!.ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_CommentBetweenEntries_PreservesTheWholeList()
    {
        var loaded = Load(WrapCommandChildren("""
            <ContextSignificanceList>
              <!-- safe-mode significance -->
              <ContextSignificance>
                <ContextMatch><Comparison parameterRef="Mode" value="1"/></ContextMatch>
                <Significance consequenceLevel="critical"/>
              </ContextSignificance>
            </ContextSignificanceList>
            """));
        var metaCommand = loaded.CommandMetaData!.MetaCommands.Single();

        Assert.Null(metaCommand.ContextSignificances);
        Assert.Equal("ContextSignificanceList", Assert.Single(metaCommand.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_BooleanExpressionContext_StaysModeledWithPreservedCriteria()
    {
        var loaded = Load(WrapCommandChildren("""
            <ContextSignificanceList>
              <ContextSignificance>
                <ContextMatch>
                  <BooleanExpression>
                    <Condition><ParameterInstanceRef parameterRef="Mode"/><ComparisonOperator>==</ComparisonOperator><Value>1</Value></Condition>
                  </BooleanExpression>
                </ContextMatch>
                <Significance consequenceLevel="critical"/>
              </ContextSignificance>
            </ContextSignificanceList>
            """));
        var entry = Assert.Single(loaded.CommandMetaData!.MetaCommands.Single().ContextSignificances!);

        Assert.Null(entry.RawXml);
        Assert.Equal("BooleanExpression", Assert.Single(entry.Context!.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }
}
