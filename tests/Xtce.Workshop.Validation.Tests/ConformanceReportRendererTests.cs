using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>Text rendering of the conformance report, shared by CLI and API.</summary>
public class ConformanceReportRendererTests
{
    [Test]
    public void ToText_CarriesDocumentIdentity_AllRows_AndSummary()
    {
        var report = ConformanceReportBuilder.Build(new SpaceSystem("Sat", []));
        var generatedAt = new DateTimeOffset(2026, 8, 20, 12, 34, 56, TimeSpan.Zero);

        var text = ConformanceReportRenderer.ToText(report, "Sat", generatedAt);

        Assert.StartsWith("XTCE 1.2 conformance report: Sat", text);
        Assert.Contains("Generated: 2026-08-20 12:34:56Z", text);
        Assert.Contains("Schema validation: VALID", text);
        for (var candidate = 1; candidate <= 109; candidate++)
        {
            Assert.Contains($"#{candidate} ", text);
        }
        Assert.Contains("Rules executed:", text);
        Assert.Contains("Summary: ", text);
    }

    [Test]
    public void ToText_ShowsFindingsOnFailingRows()
    {
        var document = new SpaceSystem("Sat", [], new TelemetryMetaData(
            [
                new ParameterTypeDefinition("Mode", ParameterTypeKind.Enumerated,
                    InitialValue: "BAD", Enumerations: [new EnumerationEntry(0, "OK")]),
            ],
            []));

        var text = ConformanceReportRenderer.ToText(
            ConformanceReportBuilder.Build(document), "Sat", DateTimeOffset.UnixEpoch);

        Assert.Contains("FAIL", text);
        Assert.Contains("-> error @ Sat/ParameterTypeSet/Mode:", text);
    }
}
