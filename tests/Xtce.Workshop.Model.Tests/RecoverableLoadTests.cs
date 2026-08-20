using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Best-effort loading: every unparseable modeled element is quarantined verbatim with a
/// positioned diagnostic, siblings keep loading, and one pass reports ALL model errors.
/// Recovery must never silently alter content — quarantined elements round-trip exactly.
/// </summary>
public class RecoverableLoadTests
{
    private static XtceLoadResult Load(string xml) =>
        XtceDocumentReader.LoadWithRecovery(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private const string MultiErrorDocument = """
        <?xml version="1.0" encoding="UTF-8"?>
        <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
          <TelemetryMetaData>
            <ParameterTypeSet>
              <IntegerParameterType name="T"/>
              <IntegerParameterType sizeInBits="8"/>
            </ParameterTypeSet>
            <ParameterSet>
              <Parameter name="Good" parameterTypeRef="T"/>
              <Parameter name="NoTypeRef"/>
              <Parameter parameterTypeRef="T"/>
            </ParameterSet>
            <ContainerSet>
              <SequenceContainer name="Ok"><EntryList/></SequenceContainer>
              <SequenceContainer name="BadCriteria"><EntryList/>
                <BaseContainer containerRef="Ok"><RestrictionCriteria>
                  <Comparison parameterRef="Good"/>
                </RestrictionCriteria></BaseContainer>
              </SequenceContainer>
            </ContainerSet>
          </TelemetryMetaData>
        </SpaceSystem>
        """;

    [Test]
    public void MultiErrorDocument_ReportsEveryProblemInOnePass_AndKeepsTheGoodParts()
    {
        var result = Load(MultiErrorDocument);

        Assert.NotNull(result.Document);
        // One broken type, two broken parameters, one broken container = 4 diagnostics.
        Assert.Equal(4, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, d => Assert.Equal(LoadDiagnosticKind.ModelError, d.Kind));
        // Positions are DOCUMENT lines, not fragment-relative: the broken type is on
        // line 6 of the fixture, the broken parameters on 10 and 11, the container on 15.
        Assert.Equal([6, 10, 11, 15], result.Diagnostics.Select(d => d.Line));

        var telemetry = result.Document!.TelemetryMetaData!;
        Assert.Equal(["T"], telemetry.ParameterTypeSet.Select(t => t.Name));
        Assert.Equal(["Good"], telemetry.ParameterSet.Select(p => p.Name));
        Assert.Equal(["Ok"], telemetry.ContainerSet!.Select(c => c.Name));
    }

    [Test]
    public void Diagnostics_CarryElementPaths()
    {
        var result = Load(MultiErrorDocument);
        var paths = result.Diagnostics.Select(d => d.Path).ToList();

        Assert.Contains("Sat/ParameterTypeSet/IntegerParameterType", paths);
        Assert.Contains("Sat/ParameterSet/Parameter[NoTypeRef]", paths);
        Assert.Contains("Sat/ParameterSet/Parameter", paths);
        Assert.Contains("Sat/ContainerSet/SequenceContainer[BadCriteria]", paths);
    }

    [Test]
    public void QuarantinedElements_RoundTripVerbatim()
    {
        var result = Load(MultiErrorDocument);

        var written = XtceDocumentWriter.Write(result.Document!);
        // No '>' on element matches — quarantined fragments carry the inherited xmlns,
        // like every preserved fragment.
        Assert.Contains("""<Parameter name="NoTypeRef" """, written);
        Assert.Contains("BadCriteria", written);
        Assert.Contains("<Comparison parameterRef=\"Good\"", written);

        // A second best-effort pass over the written output sees the same problems —
        // nothing was silently dropped or repaired.
        var reload = Load(written);
        Assert.Equal(4, reload.Diagnostics.Count);
    }

    [Test]
    public void MalformedXml_YieldsNullDocumentWithPosition()
    {
        var result = Load("<SpaceSystem name='X'><Unclosed>");

        Assert.Null(result.Document);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(LoadDiagnosticKind.MalformedXml, diagnostic.Kind);
        Assert.NotNull(diagnostic.Line);
    }

    [Test]
    public void UnusableRoot_YieldsNullDocumentWithDiagnostic()
    {
        var result = Load("""<SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204"/>""");

        Assert.Null(result.Document);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(LoadDiagnosticKind.ModelError, diagnostic.Kind);
        Assert.Contains("name", diagnostic.Message);
    }

    [Test]
    public void CleanDocument_LoadsIdenticallyToTheStrictPath_WithZeroDiagnostics()
    {
        using var strictStream = File.OpenRead(Path.Combine(TestPaths.RepoRoot, "samples", "demo-mission-1.2.xml"));
        var strict = XtceDocumentReader.Load(strictStream);

        using var recoveryStream = File.OpenRead(Path.Combine(TestPaths.RepoRoot, "samples", "demo-mission-1.2.xml"));
        var recovered = XtceDocumentReader.LoadWithRecovery(recoveryStream);

        Assert.Empty(recovered.Diagnostics);
        Assert.Equal(strict, recovered.Document);
    }

    [Test]
    public void BrokenMetaCommandAndMessage_AreQuarantinedToo()
    {
        var result = Load("""
            <?xml version="1.0" encoding="UTF-8"?>
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
              <TelemetryMetaData>
                <MessageSet>
                  <Message name="NoContainerRef"><MatchCriteria><Comparison parameterRef="P" value="1"/></MatchCriteria></Message>
                </MessageSet>
              </TelemetryMetaData>
              <CommandMetaData><MetaCommandSet>
                <MetaCommand/>
                <MetaCommand name="Fine"/>
              </MetaCommandSet></CommandMetaData>
            </SpaceSystem>
            """);

        Assert.NotNull(result.Document);
        Assert.Equal(2, result.Diagnostics.Count);
        Assert.Empty(result.Document!.TelemetryMetaData!.MessageSet!.Messages);
        Assert.Equal(["Fine"], result.Document.CommandMetaData!.MetaCommands.Select(m => m.Name));
    }
}
