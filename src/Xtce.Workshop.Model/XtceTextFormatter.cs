using System.Text;
using System.Xml;

namespace Xtce.Workshop.Model;

/// <summary>
/// Opt-in pretty-printer for the source view. Reformats ONLY inter-element whitespace:
/// the reader drops insignificant whitespace and the indenting writer never injects
/// whitespace into mixed content, so text nodes, attribute values, comments, and CDATA
/// pass through untouched. Known edge: whitespace-only element text is insignificant to
/// the reader and collapses.
/// </summary>
public static class XtceTextFormatter
{
    public static string Format(string xml)
    {
        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true,
        };
        var writerSettings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = !xml.TrimStart().StartsWith("<?xml", StringComparison.Ordinal),
        };

        var output = new Utf8StringWriter();
        using (var reader = XmlReader.Create(new StringReader(xml), readerSettings))
        using (var writer = XmlWriter.Create(output, writerSettings))
        {
            while (!reader.EOF)
            {
                writer.WriteNode(reader, defattr: false);
            }
        }
        return output.ToString();
    }

    /// <summary>StringWriter reports utf-16 by default, which would lie in the XML declaration.</summary>
    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
