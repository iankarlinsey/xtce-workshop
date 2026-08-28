using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Issue #113: the NameDescription trio (LongDescription, AliasSet, AncillaryDataSet) is
/// modeled on the named constructs — lossless, order-correct, and alias-searchable.
/// </summary>
public class DescriptionTrioTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string Sample => $"""
        <SpaceSystem xmlns="{Ns}" name="S">
          <TelemetryMetaData>
            <ParameterTypeSet>
              <IntegerParameterType name="T">
                <LongDescription>Counts things. May include &lt;b&gt;markup&lt;/b&gt;.</LongDescription>
                <AliasSet>
                  <Alias nameSpace="ops" alias="CNT_T"/>
                  <Alias nameSpace="fsw" alias="counter_t"/>
                </AliasSet>
                <AncillaryDataSet>
                  <AncillaryData name="scale" mimeType="text/plain">x10</AncillaryData>
                </AncillaryDataSet>
                <IntegerDataEncoding sizeInBits="8"/>
              </IntegerParameterType>
            </ParameterTypeSet>
            <ParameterSet>
              <Parameter name="P" parameterTypeRef="T">
                <AliasSet><Alias nameSpace="ops" alias="THE_COUNTER"/></AliasSet>
              </Parameter>
            </ParameterSet>
          </TelemetryMetaData>
        </SpaceSystem>
        """;

    [Test]
    public void Load_ModelsTheTrio_AndSearchFindsModeledAliases()
    {
        var loaded = Load(Sample);
        var type = loaded.TelemetryMetaData!.ParameterTypeSet.Single();

        Assert.Contains("<b>markup</b>", type.Description!.LongDescription);
        Assert.Equal(["CNT_T", "counter_t"], type.Description.Aliases!.Select(a => a.Alias));
        var row = Assert.Single(type.Description.AncillaryData!);
        Assert.Equal(("scale", "x10", "text/plain"), (row.Name, row.Value, row.MimeType));
        Assert.Null(type.Preserved);

        var matches = Xtce.Workshop.Validation.XtceDocumentQuery.Search(loaded, "THE_COUNTER");
        var match = Assert.Single(matches);
        Assert.Equal(("Parameter", "P", "THE_COUNTER"), (match.Kind, match.Name, match.MatchedAlias));
    }

    [Test]
    public void RoundTrip_IsLosslessAndSchemaValid_WithTrioFirstInOrder()
    {
        var loaded = Load(Sample);

        var xml = XtceDocumentWriter.Write(loaded);
        Assert.Equal(loaded, Load(xml));
        var errors = XsdValidation.Validate(xml);
        Assert.True(errors.Count == 0, "Writer output failed XSD validation:\n" + string.Join("\n", errors));
        // NameDescription children come before everything else in the type.
        Assert.True(xml.IndexOf("<LongDescription", StringComparison.Ordinal)
                    < xml.IndexOf("<AliasSet", StringComparison.Ordinal));
        Assert.True(xml.IndexOf("<AncillaryDataSet", StringComparison.Ordinal)
                    < xml.IndexOf("<IntegerDataEncoding", StringComparison.Ordinal));
    }
}
