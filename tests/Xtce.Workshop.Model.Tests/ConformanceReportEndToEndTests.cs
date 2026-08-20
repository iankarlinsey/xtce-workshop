using System.Text;
using Xtce.Workshop.Validation;
using Xunit;
using E2E = Xtce.Workshop.Model.Tests.AdversarialEndToEndTests;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Issue #48 ratchets for the per-candidate conformance report. The claim under test:
/// every one of the 109 Phase A candidates gets an explicit code-executed result, and the
/// candidate tags on findings are REAL — for each candidate the report claims to check,
/// there is a schema-valid trigger document whose report row goes FAIL with a finding
/// carrying that exact candidate number. No tag can be decorative.
/// </summary>
public class ConformanceReportEndToEndTests
{
    // ---- one trigger document per TAGGED candidate site --------------------------------

    public static TheoryData<int, string> CandidateTriggers()
    {
        var data = new TheoryData<int, string>
        {
            { 3, E2E.FixedValueDoc(binaryValue: "5A", sizeInBits: 16) },
            { 5, E2E.ArrayDoc(entryDims: "<Dimension><StartingIndex><FixedValue>0</FixedValue></StartingIndex><EndingIndex><FixedValue>1</FixedValue></EndingIndex></Dimension>") },
            { 6, E2E.ArrayDoc(entryDims: E2E.TwoDims("9", "1")) },
            { 10, ContainerSegmentDoc(orderA: "1", orderB: "1") },
            { 12, E2E.LocationDoc(fixedValue: "-8") },
            { 13, E2E.SegmentDoc(orderA: "1", orderB: "1") },
            { 16, E2E.MessageDoc(target: "AbstractBase") },
            { 19, E2E.NextContainerDoc(next: "NoSuchContainer") },
            { 27, E2E.ConstantParameterDoc(readOnly: "false") },
            { 29, E2E.InitialValueDoc(value: "9999") },
            { 48, E2E.VerifierDoc(childValue: "1") },
            { 49, E2E.BinaryEncodingDoc("""<Checksum name="custom" bitsFromReference="0"/>""") },
            { 55, E2E.SplineDoc(order: 2, points: 2) },
            {
                59, E2E.Doc("""
                    <TelemetryMetaData><ParameterTypeSet><AbsoluteTimeParameterType name="T"/></ParameterTypeSet></TelemetryMetaData>
                    """)
            },
            {
                61, E2E.Doc("""
                    <TelemetryMetaData><ParameterTypeSet>
                      <IntegerParameterType name="E"/>
                      <ArrayParameterType name="A" arrayTypeRef="E"><DimensionList>
                        <Dimension><StartingIndex><FixedValue>5</FixedValue></StartingIndex><EndingIndex><FixedValue>2</FixedValue></EndingIndex></Dimension>
                      </DimensionList></ArrayParameterType>
                    </ParameterTypeSet></TelemetryMetaData>
                    """)
            },
            { 63, E2E.EnumDoc(initialValue: "UNKNOWN") },
            { 88, ComparisonValueDoc(value: "9999") },
            {
                91, E2E.Doc("""
                    <TelemetryMetaData><ParameterSet><Parameter name="P" parameterTypeRef="NoSuchType"/></ParameterSet></TelemetryMetaData>
                    """)
            },
            {
                106, E2E.Doc("""
                    <TelemetryMetaData><ParameterTypeSet>
                      <AbsoluteTimeParameterType name="T"><Encoding units="days"><IntegerDataEncoding/></Encoding></AbsoluteTimeParameterType>
                    </ParameterTypeSet></TelemetryMetaData>
                    """)
            },
        };
        return data;
    }

    /// <summary>Candidate #10's site: ContainerSegmentRefEntry (SegmentDoc covers #13's).</summary>
    private static string ContainerSegmentDoc(string orderA, string orderB) => E2E.Doc($"""
        <TelemetryMetaData>
          <ContainerSet>
            <SequenceContainer name="Piece"><EntryList/></SequenceContainer>
            <SequenceContainer name="Frame"><EntryList>
              <ContainerSegmentRefEntry containerRef="Piece" order="{orderA}" sizeInBits="4"/>
              <ContainerSegmentRefEntry containerRef="Piece" order="{orderB}" sizeInBits="4"/>
            </EntryList></SequenceContainer>
          </ContainerSet>
        </TelemetryMetaData>
        """);

    /// <summary>Candidate #88's site: a RestrictionCriteria Comparison value checked against the parameter's type.</summary>
    private static string ComparisonValueDoc(string value) => E2E.Doc($"""
        <TelemetryMetaData>
          <ParameterTypeSet><IntegerParameterType name="U8" signed="false" sizeInBits="8"/></ParameterTypeSet>
          <ParameterSet><Parameter name="P" parameterTypeRef="U8"/></ParameterSet>
          <ContainerSet>
            <SequenceContainer name="Base"><EntryList/></SequenceContainer>
            <SequenceContainer name="Sub"><EntryList/>
              <BaseContainer containerRef="Base"><RestrictionCriteria><Comparison parameterRef="P" value="{value}"/></RestrictionCriteria></BaseContainer>
            </SequenceContainer>
          </ContainerSet>
        </TelemetryMetaData>
        """);

    [Theory]
    [MemberData(nameof(CandidateTriggers))]
    public void EveryTaggedCandidate_HasARealTrigger_ThatFailsExactlyThatReportRow(int candidateNumber, string triggerXml)
    {
        var schemaErrors = XsdValidation.Validate(triggerXml);
        Assert.True(schemaErrors.Count == 0,
            $"candidate #{candidateNumber} trigger must be schema-valid:\n" + string.Join("\n", schemaErrors));

        var document = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(triggerXml)));
        var report = ConformanceReportBuilder.Build(document);

        var row = Assert.Single(report.Candidates, c => c.CandidateNumber == candidateNumber);
        Assert.Equal(CandidateStatus.Fail, row.Status);
        Assert.Contains(row.Findings, f => f.CandidateNumber == candidateNumber);
    }

    // ---- report shape ratchets ----------------------------------------------------------

    [Fact]
    public void Report_HasExactlyCandidates1Through109_EachWithAnExplicitStatus()
    {
        var report = ConformanceReportBuilder.Build(LoadDemoMission());

        Assert.Equal(Enumerable.Range(1, 109), report.Candidates.Select(c => c.CandidateNumber));
        Assert.Equal(109, report.Summary.Values.Sum());
    }

    [Fact]
    public void DemoMission_ReportsNoFailures_AndSchemaPassOnEveryRedundantRow()
    {
        var report = ConformanceReportBuilder.Build(LoadDemoMission());

        Assert.True(report.SchemaValid, string.Join("\n", report.SchemaErrors));
        Assert.DoesNotContain(report.Candidates, c => c.Status is CandidateStatus.Fail or CandidateStatus.SchemaFail);

        // Disposition -> status-space discipline: no row's status can drift out of its lane.
        foreach (var row in report.Candidates)
        {
            switch (row.Disposition)
            {
                case "SEMANTIC":
                    Assert.True(row.Status is CandidateStatus.Pass or CandidateStatus.NotEvaluated,
                        $"#{row.CandidateNumber}: {row.Status}");
                    break;
                case "REDUNDANT":
                    Assert.Equal(CandidateStatus.SchemaPass, row.Status);
                    break;
                case "NON_NORMATIVE":
                    Assert.Equal(CandidateStatus.NotApplicable, row.Status);
                    Assert.False(string.IsNullOrWhiteSpace(row.Notes), $"#{row.CandidateNumber} needs its triage reason");
                    break;
                case "FLAGGED":
                    Assert.Equal(CandidateStatus.Info, row.Status);
                    break;
                default:
                    Assert.Fail($"#{row.CandidateNumber}: unknown disposition '{row.Disposition}'");
                    break;
            }
        }

        // The triage split the report is built on: 29 SEMANTIC / 7 REDUNDANT / 71 NON_NORMATIVE / 2 FLAGGED.
        Assert.Equal(29, report.Candidates.Count(c => c.Disposition == "SEMANTIC"));
        Assert.Equal(7, report.Candidates.Count(c => c.Disposition == "REDUNDANT"));
        Assert.Equal(71, report.Candidates.Count(c => c.Disposition == "NON_NORMATIVE"));
        Assert.Equal(2, report.Candidates.Count(c => c.Disposition == "FLAGGED"));

        // All 23 rules executed.
        Assert.Equal(23, report.Rules.Count);
        Assert.All(report.Rules, r => Assert.True(r.Executed));
    }

    [Fact]
    public void SchemaInvalidDocument_FlipsRedundantRowsToSchemaFail()
    {
        // The reader preserves unknown attributes, the writer re-emits them, and the
        // report's REAL schema validation must then reject the round-tripped document.
        var xml = """<?xml version="1.0" encoding="UTF-8"?><SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Bad" bogusAttribute="x"/>""";
        var document = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        var report = ConformanceReportBuilder.Build(document);

        Assert.False(report.SchemaValid);
        Assert.NotEmpty(report.SchemaErrors);
        Assert.All(report.Candidates.Where(c => c.Disposition == "REDUNDANT"),
            c => Assert.Equal(CandidateStatus.SchemaFail, c.Status));
    }

    [Fact]
    public void EmbeddedTriageLog_MatchesTheResearchCsv_RowForRow()
    {
        var repoCsvPath = Path.Combine(TestPaths.RepoRoot, "research", "xtce-1.2-triage-log.csv");
        var repoLines = File.ReadAllLines(repoCsvPath).Skip(1).Where(l => l.Length > 0).ToList();
        var embedded = ConformanceReportBuilder.Candidates;

        Assert.Equal(repoLines.Count, embedded.Count);
        foreach (var candidate in embedded)
        {
            // Cheap but unambiguous: the row for this number must open with the same
            // number,owner,line prefix and carry the same disposition token.
            var line = Assert.Single(repoLines, l => l.StartsWith($"{candidate.Number},", StringComparison.Ordinal));
            Assert.Contains($",{candidate.Disposition},", line);
        }
    }

    private static SpaceSystem LoadDemoMission()
    {
        var path = Path.Combine(TestPaths.RepoRoot, "samples", "demo-mission-1.2.xml");
        using var stream = File.OpenRead(path);
        return XtceDocumentReader.Load(stream);
    }
}
