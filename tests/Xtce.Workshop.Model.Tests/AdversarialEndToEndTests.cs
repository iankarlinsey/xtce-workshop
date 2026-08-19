using System.Text;
using Xtce.Workshop.Validation;
using Xunit;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Phase E of the validation pipeline (issue #34): adversarial end-to-end verification of
/// every rule in research/xtce-1.2-rule-matrix.csv. For each rule, a TRIGGER document and
/// a NEAR-MISS document, both:
/// - loaded through the REAL reader (never hand-constructed records, so reader/rule
///   assumption mismatches can't hide), and
/// - asserted XSD-VALID first — the adversarial teeth: if a trigger were schema-invalid,
///   the "semantic rule" would be re-checking what the schema already enforces, and the
///   Phase B SEMANTIC/REDUNDANT triage line for that rule would be wrong.
/// </summary>
public class AdversarialEndToEndTests
{
    public static TheoryData<string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string>();

        data.Add("XTCE-1.2-R01-ambiguous-time-units-flagged",
            Doc("""
                <TelemetryMetaData><ParameterTypeSet>
                  <AbsoluteTimeParameterType name="T"><Encoding units="days"><IntegerDataEncoding/></Encoding></AbsoluteTimeParameterType>
                </ParameterTypeSet></TelemetryMetaData>
                """),
            Doc("""
                <TelemetryMetaData><ParameterTypeSet>
                  <AbsoluteTimeParameterType name="T"><Encoding units="seconds"><IntegerDataEncoding/></Encoding></AbsoluteTimeParameterType>
                </ParameterTypeSet></TelemetryMetaData>
                """));

        data.Add("XTCE-1.2-R02-array-dim-count-match-type",
            ArrayDoc(entryDims: "<Dimension><StartingIndex><FixedValue>0</FixedValue></StartingIndex><EndingIndex><FixedValue>1</FixedValue></EndingIndex></Dimension>"),
            ArrayDoc(entryDims: TwoDims("1", "1")));

        data.Add("XTCE-1.2-R03-checksum-custom-requires-inputalgorithm",
            BinaryEncodingDoc("""<Checksum name="custom" bitsFromReference="0"/>"""),
            BinaryEncodingDoc("""<Checksum name="custom" bitsFromReference="0"><InputAlgorithm name="algo"><AlgorithmText>x</AlgorithmText></InputAlgorithm></Checksum>"""));

        data.Add("XTCE-1.2-R04-container-segments-no-overlap",
            // Orders are 1-based here: `order` is PositiveLongType (min 1), even though
            // the XSD's own documentation says "the first segment order='0'" — a
            // spec-internal inconsistency this Phase E suite discovered (see the research
            // README's FLAGGED findings).
            SegmentDoc(orderA: "1", orderB: "1"),
            SegmentDoc(orderA: "1", orderB: "2"));

        data.Add("XTCE-1.2-R05-dim-subset-lt-type",
            ArrayDoc(entryDims: TwoDims("9", "1")),
            ArrayDoc(entryDims: TwoDims("1", "1")));

        data.Add("XTCE-1.2-R06-dimensionlist-order-must-ascend",
            Doc("""
                <TelemetryMetaData><ParameterTypeSet>
                  <IntegerParameterType name="E"/>
                  <ArrayParameterType name="A" arrayTypeRef="E"><DimensionList>
                    <Dimension><StartingIndex><FixedValue>5</FixedValue></StartingIndex><EndingIndex><FixedValue>2</FixedValue></EndingIndex></Dimension>
                  </DimensionList></ArrayParameterType>
                </ParameterTypeSet></TelemetryMetaData>
                """),
            Doc("""
                <TelemetryMetaData><ParameterTypeSet>
                  <IntegerParameterType name="E"/>
                  <ArrayParameterType name="A" arrayTypeRef="E"><DimensionList>
                    <Dimension><StartingIndex><FixedValue>2</FixedValue></StartingIndex><EndingIndex><FixedValue>5</FixedValue></EndingIndex></Dimension>
                  </DimensionList></ArrayParameterType>
                </ParameterTypeSet></TelemetryMetaData>
                """));

        data.Add("XTCE-1.2-R07-enum-initial-value-must-be-valid-label",
            EnumDoc(initialValue: "UNKNOWN"),
            EnumDoc(initialValue: "SAFE"));

        data.Add("XTCE-1.2-R08-location-in-container-flags",
            LocationDoc(fixedValue: "-8"),
            LocationDoc(fixedValue: "8"));

        data.Add("XTCE-1.2-R09-messagetype-containerref-must-be-root",
            MessageDoc(target: "AbstractBase"),
            MessageDoc(target: "WholePacket"));

        data.Add("XTCE-1.2-R10-nextcontainer-ref-must-resolve",
            NextContainerDoc(next: "NoSuchContainer"),
            NextContainerDoc(next: "Base"));

        data.Add("XTCE-1.2-R11-no-dangling-name-references",
            Doc("""
                <TelemetryMetaData><ParameterSet><Parameter name="P" parameterTypeRef="NoSuchType"/></ParameterSet></TelemetryMetaData>
                """),
            Doc("""
                <TelemetryMetaData>
                  <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
                  <ParameterSet><Parameter name="P" parameterTypeRef="T"/></ParameterSet>
                </TelemetryMetaData>
                """));

        data.Add("XTCE-1.2-R12-no-duplicate-verifiers-post-inheritance",
            VerifierDoc(childValue: "1"),
            VerifierDoc(childValue: "2"));

        data.Add("XTCE-1.2-R13-spline-order-requires-min-points",
            SplineDoc(order: 2, points: 2),
            SplineDoc(order: 2, points: 3));

        data.Add("XTCE-1.2-R14-time-datatype-requires-encoding",
            Doc("""
                <TelemetryMetaData><ParameterTypeSet><AbsoluteTimeParameterType name="T"/></ParameterTypeSet></TelemetryMetaData>
                """),
            Doc("""
                <TelemetryMetaData><ParameterTypeSet>
                  <AbsoluteTimeParameterType name="T"><Encoding units="seconds"><IntegerDataEncoding/></Encoding></AbsoluteTimeParameterType>
                </ParameterTypeSet></TelemetryMetaData>
                """));

        data.Add("XTCE-1.2-R15-typed-value-valid-for-type",
            InitialValueDoc(value: "9999"),
            InitialValueDoc(value: "99"));

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Trigger_IsSchemaValid_LoadsThroughTheReader_AndFiresTheRule(
        string ruleId, string triggerXml, string nearMissXml)
    {
        _ = nearMissXml;

        var schemaErrors = XsdValidation.Validate(triggerXml);
        Assert.True(schemaErrors.Count == 0,
            $"{ruleId} TRIGGER must be schema-valid (otherwise the rule re-checks the schema):\n" +
            string.Join("\n", schemaErrors));

        var document = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(triggerXml)));
        var issues = XtceValidator.Validate(document);

        Assert.Contains(issues, i => i.RuleId == ruleId);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NearMiss_IsSchemaValid_AndDoesNotFireTheRule(
        string ruleId, string triggerXml, string nearMissXml)
    {
        _ = triggerXml;

        var schemaErrors = XsdValidation.Validate(nearMissXml);
        Assert.True(schemaErrors.Count == 0,
            $"{ruleId} NEAR-MISS must be schema-valid:\n" + string.Join("\n", schemaErrors));

        var document = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(nearMissXml)));
        var issues = XtceValidator.Validate(document);

        Assert.DoesNotContain(issues, i => i.RuleId == ruleId);
    }

    [Fact]
    public void EveryMatrixRule_HasAPhaseECase()
    {
        var matrix = File.ReadAllLines(Path.Combine(TestPaths.RepoRoot, "research", "xtce-1.2-rule-matrix.csv"))
            .Skip(1)
            .Select(line => line.Split(',')[0])
            .Where(id => id.StartsWith("XTCE-", StringComparison.Ordinal))
            .ToHashSet();
        var covered = Cases().Select(c => (string)c[0]).ToHashSet();

        Assert.True(matrix.SetEquals(covered),
            "Matrix rules without a Phase E case: " + string.Join(", ", matrix.Except(covered)) +
            " | Phase E cases without a matrix rule: " + string.Join(", ", covered.Except(matrix)));
    }

    // ---- document builders (each returns a complete, schema-valid XTCE document) --------

    private static string Doc(string body) =>
        $"""<?xml version="1.0" encoding="UTF-8"?><SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="E2E">{body}</SpaceSystem>""";

    private static string TwoDims(string endA, string endB) =>
        $"<Dimension><StartingIndex><FixedValue>0</FixedValue></StartingIndex><EndingIndex><FixedValue>{endA}</FixedValue></EndingIndex></Dimension>" +
        $"<Dimension><StartingIndex><FixedValue>0</FixedValue></StartingIndex><EndingIndex><FixedValue>{endB}</FixedValue></EndingIndex></Dimension>";

    private static string ArrayDoc(string entryDims) => Doc($"""
        <TelemetryMetaData>
          <ParameterTypeSet>
            <IntegerParameterType name="E"/>
            <ArrayParameterType name="A" arrayTypeRef="E"><DimensionList>{TwoDims("3", "3")}</DimensionList></ArrayParameterType>
          </ParameterTypeSet>
          <ParameterSet><Parameter name="Arr" parameterTypeRef="A"/></ParameterSet>
          <ContainerSet><SequenceContainer name="Frame"><EntryList>
            <ArrayParameterRefEntry parameterRef="Arr"><DimensionList>{entryDims}</DimensionList></ArrayParameterRefEntry>
          </EntryList></SequenceContainer></ContainerSet>
        </TelemetryMetaData>
        """);

    private static string BinaryEncodingDoc(string checksum) => Doc($"""
        <TelemetryMetaData><ParameterTypeSet>
          <BinaryParameterType name="B">
            <BinaryDataEncoding>
              <ErrorDetectCorrect>{checksum}</ErrorDetectCorrect>
              <SizeInBits><FixedValue>32</FixedValue></SizeInBits>
            </BinaryDataEncoding>
          </BinaryParameterType>
        </ParameterTypeSet></TelemetryMetaData>
        """);

    private static string SegmentDoc(string orderA, string orderB) => Doc($"""
        <TelemetryMetaData>
          <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
          <ParameterSet><Parameter name="P" parameterTypeRef="T"/></ParameterSet>
          <ContainerSet><SequenceContainer name="Frame"><EntryList>
            <ParameterSegmentRefEntry parameterRef="P" order="{orderA}" sizeInBits="4"/>
            <ParameterSegmentRefEntry parameterRef="P" order="{orderB}" sizeInBits="4"/>
          </EntryList></SequenceContainer></ContainerSet>
        </TelemetryMetaData>
        """);

    private static string EnumDoc(string initialValue) => Doc($"""
        <TelemetryMetaData><ParameterTypeSet>
          <EnumeratedParameterType name="Mode" initialValue="{initialValue}">
            <EnumerationList><Enumeration value="0" label="SAFE"/></EnumerationList>
          </EnumeratedParameterType>
        </ParameterTypeSet></TelemetryMetaData>
        """);

    private static string LocationDoc(string fixedValue) => Doc($"""
        <TelemetryMetaData>
          <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
          <ParameterSet><Parameter name="P" parameterTypeRef="T"/></ParameterSet>
          <ContainerSet><SequenceContainer name="Frame"><EntryList>
            <ParameterRefEntry parameterRef="P">
              <LocationInContainerInBits referenceLocation="containerStart"><FixedValue>{fixedValue}</FixedValue></LocationInContainerInBits>
            </ParameterRefEntry>
          </EntryList></SequenceContainer></ContainerSet>
        </TelemetryMetaData>
        """);

    private static string MessageDoc(string target) => Doc($"""
        <TelemetryMetaData>
          <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
          <ParameterSet><Parameter name="P" parameterTypeRef="T"/></ParameterSet>
          <ContainerSet>
            <SequenceContainer name="AbstractBase" abstract="true"><EntryList/></SequenceContainer>
            <SequenceContainer name="WholePacket"><EntryList/>
              <BaseContainer containerRef="AbstractBase"><RestrictionCriteria><Comparison parameterRef="P" value="1"/></RestrictionCriteria></BaseContainer>
            </SequenceContainer>
          </ContainerSet>
          <MessageSet>
            <Message name="M"><MatchCriteria><Comparison parameterRef="P" value="1"/></MatchCriteria><ContainerRef containerRef="{target}"/></Message>
          </MessageSet>
        </TelemetryMetaData>
        """);

    private static string NextContainerDoc(string next) => Doc($"""
        <TelemetryMetaData>
          <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
          <ParameterSet><Parameter name="P" parameterTypeRef="T"/></ParameterSet>
          <ContainerSet>
            <SequenceContainer name="Base"><EntryList/></SequenceContainer>
            <SequenceContainer name="Sub"><EntryList/>
              <BaseContainer containerRef="Base"><RestrictionCriteria>
                <Comparison parameterRef="P" value="1"/>
                <NextContainer containerRef="{next}"/>
              </RestrictionCriteria></BaseContainer>
            </SequenceContainer>
          </ContainerSet>
        </TelemetryMetaData>
        """);

    private static string VerifierDoc(string childValue) => Doc($"""
        <TelemetryMetaData>
          <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
          <ParameterSet><Parameter name="Ack" parameterTypeRef="T"/></ParameterSet>
        </TelemetryMetaData>
        <CommandMetaData><MetaCommandSet>
          <MetaCommand name="Base" abstract="true"><VerifierSet>
            <CompleteVerifier><Comparison parameterRef="Ack" value="1"/><CheckWindow timeToStopChecking="PT5S"/></CompleteVerifier>
          </VerifierSet></MetaCommand>
          <MetaCommand name="Child"><BaseMetaCommand metaCommandRef="Base"/><VerifierSet>
            <CompleteVerifier><Comparison parameterRef="Ack" value="{childValue}"/><CheckWindow timeToStopChecking="PT5S"/></CompleteVerifier>
          </VerifierSet></MetaCommand>
        </MetaCommandSet></CommandMetaData>
        """);

    private static string SplineDoc(int order, int points)
    {
        var pointXml = string.Join("", Enumerable.Range(0, points).Select(i =>
            $"""<SplinePoint raw="{i}" calibrated="{i}"/>"""));
        return Doc($"""
            <TelemetryMetaData><ParameterTypeSet>
              <IntegerParameterType name="T">
                <IntegerDataEncoding><DefaultCalibrator><SplineCalibrator order="{order}">{pointXml}</SplineCalibrator></DefaultCalibrator></IntegerDataEncoding>
              </IntegerParameterType>
            </ParameterTypeSet></TelemetryMetaData>
            """);
    }

    private static string InitialValueDoc(string value) => Doc($"""
        <TelemetryMetaData>
          <ParameterTypeSet><IntegerParameterType name="U8" signed="false" sizeInBits="8"/></ParameterTypeSet>
          <ParameterSet><Parameter name="P" parameterTypeRef="U8" initialValue="{value}"/></ParameterSet>
        </TelemetryMetaData>
        """);
}
