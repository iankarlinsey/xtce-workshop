using System.Xml;

namespace Xtce.Workshop.Model;

/// <summary>
/// Reads a SpaceSystem element (and, recursively, its nested SpaceSystem children) from
/// an XTCE document. Deliberately built on XmlReader (forward-only, non-buffering)
/// rather than XDocument/XmlDocument — XTCE files can be tens of megabytes, and the
/// reading strategy needs to survive being extended to a real streaming parser later
/// without a rewrite. See another implementation's streaming reader for the pattern this follows.
/// </summary>
public static class XtceDocumentReader
{
    private const string ExpectedRootElementName = "SpaceSystem";

    public static SpaceSystem Load(Stream xmlStream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        using var reader = XmlReader.Create(xmlStream, settings);

        try
        {
            if (reader.MoveToContent() != XmlNodeType.Element)
            {
                throw new XtceParseException("The document has no root element.");
            }

            return ReadSpaceSystem(reader);
        }
        catch (XmlException ex)
        {
            throw new XtceParseException("The document is not well-formed XML.", ex);
        }
    }

    /// <summary>
    /// Reads one SpaceSystem element, positioned at its start tag, consuming through its
    /// matching end tag (or itself, if empty) — so the caller's reader ends up positioned
    /// exactly where a sibling-or-parent's own loop expects it next.
    /// </summary>
    private static SpaceSystem ReadSpaceSystem(XmlReader reader)
    {
        if (reader.LocalName != ExpectedRootElementName)
        {
            throw new XtceParseException(
                $"Expected element '{ExpectedRootElementName}', found '{reader.LocalName}'.");
        }

        var name = reader.GetAttribute("name");
        if (string.IsNullOrEmpty(name))
        {
            throw new XtceParseException(
                "A SpaceSystem element is missing its required 'name' attribute.");
        }

        var children = new List<SpaceSystem>();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return new SpaceSystem(name, children);
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == ExpectedRootElementName)
            {
                children.Add(ReadSpaceSystem(reader));
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // Not-yet-supported child (Header, TelemetryMetaData, CommandMetaData, ...) —
                // skip its whole subtree without buffering it.
                reader.Skip();
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();

        return new SpaceSystem(name, children);
    }
}
