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

    [Test]
    public void UnmodelableTrioShapes_BailOutLosslessly()
    {
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <TelemetryMetaData>
                <ParameterTypeSet>
                  <IntegerParameterType name="T">
                    <LongDescription>Has <b xmlns="http://www.w3.org/1999/xhtml">real markup</b> children.</LongDescription>
                    <AliasSet>
                      <Alias nameSpace="ops" alias="A1"/>
                      <Alias nameSpace="broken"/>
                      <Foreign/>
                    </AliasSet>
                    <AncillaryDataSet>
                      <AncillaryData name="blob"><payload xmlns="http://example.com">x</payload></AncillaryData>
                      <AncillaryData name="plain">ok</AncillaryData>
                    </AncillaryDataSet>
                  </IntegerParameterType>
                </ParameterTypeSet>
                <ParameterSet>
                  <Parameter name="P" parameterTypeRef="T">
                    <AliasSet/>
                    <AncillaryDataSet/>
                  </Parameter>
                </ParameterSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);
        var type = loaded.TelemetryMetaData!.ParameterTypeSet.Single();

        // Element-content LongDescription stays a fragment on the construct.
        Assert.Null(type.Description!.LongDescription);
        Assert.Equal(["LongDescription"], type.Preserved!.Select(f => f.ElementName).ToList());
        // The broken alias and the foreign row ride preserved inside the set.
        Assert.Equal(["A1"], type.Description.Aliases!.Select(a => a.Alias));
        Assert.Equal(["Alias", "Foreign"], type.Description.PreservedAliases!.Select(f => f.ElementName).ToList());
        // Element-content ancillary payloads stay preserved inside their set.
        Assert.Equal(["plain"], type.Description.AncillaryData!.Select(r => r.Name));
        Assert.Equal(["AncillaryData"], type.Description.PreservedAncillaryData!.Select(f => f.ElementName).ToList());

        // Empty sets stay empty, non-null (element fidelity).
        var parameter = loaded.TelemetryMetaData.ParameterSet.Single();
        Assert.Empty(parameter.Description!.Aliases!);
        Assert.Empty(parameter.Description.AncillaryData!);

        var written = XtceDocumentWriter.Write(loaded);
        Assert.Equal(loaded, Load(written));
        Assert.Contains("<AliasSet />", written.Replace("></AliasSet>", " />")); // empty set survives
    }
}
