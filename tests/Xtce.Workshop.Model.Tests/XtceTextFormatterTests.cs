using System.Xml;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Model.Tests;

public class XtceTextFormatterTests
{
    [Test]
    public void Format_IndentsDenseElementOnlyContent()
    {
        var formatted = XtceTextFormatter.Format(
            """<SpaceSystem name="Sat"><TelemetryMetaData><ParameterSet><Parameter name="P" parameterTypeRef="T"/></ParameterSet></TelemetryMetaData></SpaceSystem>""");

        var lines = formatted.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Contains("<SpaceSystem name=\"Sat\">", lines);
        Assert.Contains("  <TelemetryMetaData>", lines);
        Assert.Contains("    <ParameterSet>", lines);
        Assert.Contains("      <Parameter name=\"P\" parameterTypeRef=\"T\" />", lines);
    }

    [Test]
    public void Format_NeverTouchesTextNodesOrMixedContent()
    {
        var formatted = XtceTextFormatter.Format(
            "<SpaceSystem name=\"Sat\"><LongDescription>  keeps   inner  spacing  </LongDescription>"
            + "<Note>mixed <b>bold</b> tail</Note></SpaceSystem>");

        Assert.Contains("<LongDescription>  keeps   inner  spacing  </LongDescription>", formatted);
        Assert.Contains("<Note>mixed <b>bold</b> tail</Note>", formatted);
    }

    [Test]
    public void Format_PreservesCommentsCdataAndAttributes()
    {
        var formatted = XtceTextFormatter.Format(
            "<SpaceSystem name=\"Sat\"><!-- license  header --><Blob><![CDATA[ raw <stuff> ]]></Blob>"
            + "<P a=\"  spaced  value \"/></SpaceSystem>");

        Assert.Contains("<!-- license  header -->", formatted);
        Assert.Contains("<![CDATA[ raw <stuff> ]]>", formatted);
        Assert.Contains("a=\"  spaced  value \"", formatted);
    }

    [Test]
    public void Format_KeepsTheXmlDeclarationAsUtf8OnlyWhenPresent()
    {
        var withDeclaration = XtceTextFormatter.Format(
            """<?xml version="1.0" encoding="UTF-8"?><SpaceSystem name="Sat"/>""");
        var withoutDeclaration = XtceTextFormatter.Format("""<SpaceSystem name="Sat"/>""");

        Assert.Contains("encoding=\"utf-8\"", withDeclaration.ToLowerInvariant());
        Assert.False(withoutDeclaration.Contains("<?xml"));
    }

    [Test]
    public void Format_MalformedXml_Throws()
    {
        Assert.Throws<XmlException>(() => XtceTextFormatter.Format("<SpaceSystem name='X'><Unclosed>"));
    }

    [Test]
    public void Format_RoundTripsThroughTheReaderIdentically()
    {
        var dense = """<SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat"><TelemetryMetaData><ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet><ParameterSet><Parameter name="P" parameterTypeRef="T"/></ParameterSet></TelemetryMetaData></SpaceSystem>""";

        using var denseStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(dense));
        using var formattedStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(XtceTextFormatter.Format(dense)));
        var fromDense = XtceDocumentReader.LoadWithRecovery(denseStream);
        var fromFormatted = XtceDocumentReader.LoadWithRecovery(formattedStream);

        Assert.Equal(0, fromFormatted.Diagnostics.Count);
        Assert.Equal(fromDense.Document!.Name, fromFormatted.Document!.Name);
        Assert.Equal(
            fromDense.Document.TelemetryMetaData!.ParameterSet.Single().Name,
            fromFormatted.Document.TelemetryMetaData!.ParameterSet.Single().Name);
        Assert.Equal(
            fromDense.Document.TelemetryMetaData.ParameterTypeSet.Single().Name,
            fromFormatted.Document.TelemetryMetaData.ParameterTypeSet.Single().Name);
    }
}
