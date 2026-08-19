using System.Xml;

namespace Xtce.Workshop.Model;

/// <summary>
/// Reads the root SpaceSystem element of an XTCE document. Deliberately built on
/// XmlReader (forward-only, non-buffering) rather than XDocument/XmlDocument, even
/// though this minimal slice only needs one attribute off the root element — XTCE
/// files can be tens of megabytes, and the reading strategy needs to survive being
/// extended to a real streaming parser later without a rewrite. See another implementation's
/// streaming reader for the pattern this follows.
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

            if (reader.LocalName != ExpectedRootElementName)
            {
                throw new XtceParseException(
                    $"Expected root element '{ExpectedRootElementName}', found '{reader.LocalName}'.");
            }

            var name = reader.GetAttribute("name");
            if (string.IsNullOrEmpty(name))
            {
                throw new XtceParseException(
                    "The SpaceSystem root element is missing its required 'name' attribute.");
            }

            return new SpaceSystem(name);
        }
        catch (XmlException ex)
        {
            throw new XtceParseException("The document is not well-formed XML.", ex);
        }
    }
}
