using System.Xml;

namespace Xtce.Workshop.Validation;

/// <summary>
/// Lightweight peeks into preserved raw-XML fragments — several rules need one attribute
/// off a fragment's root element (a preserved type's name, a time Encoding's units) without
/// modeling the construct. A malformed fragment yields null rather than an exception:
/// preserved content must never take validation down.
/// </summary>
public static class XmlFragmentInspector
{
    public static string? RootAttribute(string outerXml, string attributeName)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return reader.MoveToContent() == XmlNodeType.Element ? reader.GetAttribute(attributeName) : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
