using System.Text;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Issue #86: preserved fragments used to re-declare the XTCE namespace on every root,
/// spraying xmlns="..." across the serialized body line after line.
/// </summary>
public class WriterNamespaceTests
{
    private static XtceLoadResult LoadText(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return XtceDocumentReader.LoadWithRecovery(stream);
    }

    private const string DocumentWithPreserved = """
        <?xml version="1.0" encoding="UTF-8"?>
        <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
          <Header validationStatus="Test" date="2026-01-01"/>
          <TelemetryMetaData>
            <ParameterTypeSet>
              <IntegerParameterType name="T">
                <UnitSet/>
                <IntegerDataEncoding sizeInBits="16"/>
              </IntegerParameterType>
            </ParameterTypeSet>
            <ParameterSet><Parameter name="P" parameterTypeRef="T"/></ParameterSet>
            <AlgorithmSet><CustomAlgorithm name="A"><AlgorithmText>x = 1</AlgorithmText></CustomAlgorithm></AlgorithmSet>
          </TelemetryMetaData>
        </SpaceSystem>
        """;

    [Test]
    public void Write_DeclaresTheXtceNamespaceExactlyOnce()
    {
        var result = LoadText(DocumentWithPreserved);

        var written = XtceDocumentWriter.Write(result.Document!);

        var declarations = written.Split("xmlns=\"http://www.omg.org/spec/XTCE/20180204\"").Length - 1;
        Assert.Equal(1, declarations);
        // The preserved content itself is still all there.
        Assert.Contains("<Header", written);
        Assert.Contains("<UnitSet", written);
        Assert.Contains("IntegerDataEncoding", written);
        Assert.Contains("x = 1", written);
    }

    [Test]
    public void Write_OutputReloadsToTheSameModelWithNoDiagnostics()
    {
        var original = LoadText(DocumentWithPreserved);
        var written = XtceDocumentWriter.Write(original.Document!);

        var reloaded = LoadText(written);

        Assert.Equal(0, reloaded.Diagnostics.Count);
        Assert.Equal("P", reloaded.Document!.TelemetryMetaData!.ParameterSet.Single().Name);
        Assert.Equal("T", reloaded.Document.TelemetryMetaData.ParameterTypeSet.Single().Name);
        // The preserved Header still resolves into the XTCE namespace; the algorithm
        // is modeled since #103 and must survive the round trip.
        Assert.True(reloaded.Document.Preserved!.Any(f => f.ElementName == "Header"));
        Assert.Equal("A", reloaded.Document.TelemetryMetaData.AlgorithmSet!.Single().Name);
        Assert.Equal("x = 1", reloaded.Document.TelemetryMetaData.AlgorithmSet.Single().AlgorithmText);
    }

    [Test]
    public void Write_NamespacelessJsonFragmentStillInheritsTheDocumentNamespace()
    {
        // Documents posted as JSON can carry fragments with no xmlns of their own — the
        // UI's MatchCriteria creator used to rely on textual inheritance. They must not
        // acquire xmlns="" (which would eject them from the XTCE namespace).
        var message = new Message("M", "C", new List<RawXmlFragment>
        {
            new("MatchCriteria", "<MatchCriteria><Comparison parameterRef=\"P\" value=\"1\"/></MatchCriteria>"),
        }, null);
        var doc = new SpaceSystem("Sat", new List<SpaceSystem>(),
            new TelemetryMetaData(
                new List<ParameterTypeDefinition> { new("T", ParameterTypeKind.Integer, null, null, null, null, null, null, null, null, null, null, null) },
                new List<Parameter> { new("P", "T", null, null, null) },
                null, null, null,
                new List<SequenceContainer> { new("C", new List<SequenceEntry>(), null, null, null, null) },
                new MessageSet(new List<Message> { message }, null, null), null));

        var written = XtceDocumentWriter.Write(doc);

        Assert.False(written.Contains("xmlns=\"\""));
        var reloaded = LoadText(written);
        Assert.Equal(0, reloaded.Diagnostics.Count);
        Assert.Equal("M", reloaded.Document!.TelemetryMetaData!.MessageSet!.Messages.Single().Name);
    }

    [Test]
    public void Write_ForeignNamespaceFragmentKeepsItsDeclaration()
    {
        var xml = """
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
              <AncillaryDataSet>
                <AncillaryData name="ext"><custom:Extra xmlns:custom="http://example.com/custom">v</custom:Extra></AncillaryData>
              </AncillaryDataSet>
            </SpaceSystem>
            """;
        var result = LoadText(xml);

        var written = XtceDocumentWriter.Write(result.Document!);

        Assert.Contains("http://example.com/custom", written);
        Assert.Equal(0, LoadText(written).Diagnostics.Count);
    }
}
