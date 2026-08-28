using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Modeled BooleanExpression trees inside MatchCriteria (#124): Condition leaves and
/// recursive ANDed/ORed junctions.
/// </summary>
public class BooleanExpressionTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string WrapMessageCriteria(string criteriaChildren) => $"""
        <SpaceSystem xmlns="{Ns}" name="S">
          <TelemetryMetaData>
            <ParameterTypeSet>
              <IntegerParameterType name="U8" signed="false" sizeInBits="8">
                <UnitSet/>
              </IntegerParameterType>
            </ParameterTypeSet>
            <ParameterSet>
              <Parameter name="Apid" parameterTypeRef="U8"/>
              <Parameter name="Mode" parameterTypeRef="U8"/>
              <Parameter name="Backup" parameterTypeRef="U8"/>
            </ParameterSet>
            <ContainerSet>
              <SequenceContainer name="Frame"><EntryList/></SequenceContainer>
            </ContainerSet>
            <MessageSet>
              <Message name="Ops">
                <MatchCriteria>
                  {criteriaChildren}
                </MatchCriteria>
                <ContainerRef containerRef="Frame"/>
              </Message>
            </MessageSet>
          </TelemetryMetaData>
        </SpaceSystem>
        """;

    private static string RoundTrip(SpaceSystem loaded, out SpaceSystem reloaded)
    {
        var xml = XtceDocumentWriter.Write(loaded);
        reloaded = Load(xml);
        return xml;
    }

    private static MatchCriteria MessageCriteria(SpaceSystem document) =>
        document.TelemetryMetaData!.MessageSet!.Messages.Single().MatchCriteria!;

    [Test]
    public void Load_ModelsSingleConditionWithValue()
    {
        var loaded = Load(WrapMessageCriteria("""
            <BooleanExpression>
              <Condition>
                <ParameterInstanceRef parameterRef="Apid" instance="-1" useCalibratedValue="false"/>
                <ComparisonOperator>!=</ComparisonOperator>
                <Value>101</Value>
              </Condition>
            </BooleanExpression>
            """));
        var root = MessageCriteria(loaded).BooleanExpression!;

        Assert.Equal(BooleanNodeKind.Condition, root.Kind);
        Assert.Equal("Apid", root.Left!.ParameterRef);
        Assert.Equal(-1, root.Left.Instance);
        Assert.Equal(false, root.Left.UseCalibratedValue);
        Assert.Equal("!=", root.Operator);
        Assert.Equal("101", root.Value);
        Assert.Null(root.Right);
        Assert.Null(MessageCriteria(loaded).Preserved);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_ModelsParameterToParameterCondition()
    {
        var loaded = Load(WrapMessageCriteria("""
            <BooleanExpression>
              <Condition>
                <ParameterInstanceRef parameterRef="Mode"/>
                <ComparisonOperator>==</ComparisonOperator>
                <ParameterInstanceRef parameterRef="Backup"/>
              </Condition>
            </BooleanExpression>
            """));
        var root = MessageCriteria(loaded).BooleanExpression!;

        Assert.Equal("Mode", root.Left!.ParameterRef);
        Assert.Equal("Backup", root.Right!.ParameterRef);
        Assert.Null(root.Value);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_ModelsNestedJunctions()
    {
        var loaded = Load(WrapMessageCriteria("""
            <BooleanExpression>
              <ANDedConditions>
                <Condition>
                  <ParameterInstanceRef parameterRef="Apid"/>
                  <ComparisonOperator>==</ComparisonOperator>
                  <Value>101</Value>
                </Condition>
                <ORedConditions>
                  <Condition>
                    <ParameterInstanceRef parameterRef="Mode"/>
                    <ComparisonOperator>==</ComparisonOperator>
                    <Value>1</Value>
                  </Condition>
                  <Condition>
                    <ParameterInstanceRef parameterRef="Mode"/>
                    <ComparisonOperator>==</ComparisonOperator>
                    <Value>2</Value>
                  </Condition>
                </ORedConditions>
              </ANDedConditions>
            </BooleanExpression>
            """));
        var root = MessageCriteria(loaded).BooleanExpression!;

        Assert.Equal(BooleanNodeKind.And, root.Kind);
        Assert.Equal(2, root.Children!.Count);
        Assert.Equal(BooleanNodeKind.Condition, root.Children[0].Kind);
        var orNode = root.Children[1];
        Assert.Equal(BooleanNodeKind.Or, orNode.Kind);
        Assert.Equal(2, orNode.Children!.Count);
        Assert.Equal(3, root.Leaves().Count());

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_JunctionWithOneChild_StaysPreserved()
    {
        // Schema-invalid (choice minOccurs=2) — bail rather than model an illegal tree.
        var loaded = Load(WrapMessageCriteria("""
            <BooleanExpression>
              <ANDedConditions>
                <Condition>
                  <ParameterInstanceRef parameterRef="Apid"/>
                  <ComparisonOperator>==</ComparisonOperator>
                  <Value>101</Value>
                </Condition>
              </ANDedConditions>
            </BooleanExpression>
            """));
        var criteria = MessageCriteria(loaded);

        Assert.Null(criteria.BooleanExpression);
        Assert.Equal("BooleanExpression", Assert.Single(criteria.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_CommentInsideExpression_StaysPreserved()
    {
        var loaded = Load(WrapMessageCriteria("""
            <BooleanExpression>
              <!-- backup path -->
              <Condition>
                <ParameterInstanceRef parameterRef="Apid"/>
                <ComparisonOperator>==</ComparisonOperator>
                <Value>101</Value>
              </Condition>
            </BooleanExpression>
            """));
        var criteria = MessageCriteria(loaded);

        Assert.Null(criteria.BooleanExpression);
        Assert.Equal("BooleanExpression", Assert.Single(criteria.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_ArgumentInstanceRefCondition_StaysPreserved()
    {
        // The argument-flavoured expression types share element names but not shapes.
        var loaded = Load(WrapMessageCriteria("""
            <BooleanExpression>
              <Condition>
                <ArgumentInstanceRef argumentRef="delay"/>
                <ComparisonOperator>==</ComparisonOperator>
                <Value>5</Value>
              </Condition>
            </BooleanExpression>
            """));
        var criteria = MessageCriteria(loaded);

        Assert.Null(criteria.BooleanExpression);
        Assert.Equal("BooleanExpression", Assert.Single(criteria.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_ExpressionInsideAlarmConditionsLevel_IsModeledToo()
    {
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <TelemetryMetaData>
                <ParameterTypeSet>
                  <BooleanParameterType name="HeaterOn">
                    <UnitSet/>
                    <DefaultAlarm>
                      <AlarmConditions>
                        <WarningAlarm>
                          <BooleanExpression>
                            <ORedConditions>
                              <Condition><ParameterInstanceRef parameterRef="Mode"/><ComparisonOperator>==</ComparisonOperator><Value>1</Value></Condition>
                              <Condition><ParameterInstanceRef parameterRef="Mode"/><ComparisonOperator>==</ComparisonOperator><Value>2</Value></Condition>
                            </ORedConditions>
                          </BooleanExpression>
                        </WarningAlarm>
                      </AlarmConditions>
                    </DefaultAlarm>
                  </BooleanParameterType>
                  <IntegerParameterType name="U8" signed="false" sizeInBits="8"><UnitSet/></IntegerParameterType>
                </ParameterTypeSet>
                <ParameterSet>
                  <Parameter name="Mode" parameterTypeRef="U8"/>
                </ParameterSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);
        var warning = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "HeaterOn")
            .NonNumericDefaultAlarm!.Conditions!.Warning!;

        Assert.Equal(BooleanNodeKind.Or, warning.BooleanExpression!.Kind);
        Assert.Null(warning.Preserved);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }
}
