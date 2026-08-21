using System.Xml;

namespace Xtce.Workshop.Model;

/// <summary>
/// The declared XTCE namespace is an assessment fact: the reader matches elements by
/// local name (so legacy documents still load), and this probe reports what the document
/// claims to be so callers can lead with it.
/// </summary>
public static class XtceNamespace
{
    public const string V1_2 = "http://www.omg.org/spec/XTCE/20180204";

    /// <summary>XTCE 1.0 and 1.1 share one namespace; they are indistinguishable by it.</summary>
    public const string Legacy = "http://www.omg.org/space/xtce";

    /// <summary>
    /// The root element's namespace: "" when none is declared, null when the input has no
    /// readable root element at all.
    /// </summary>
    public static string? ReadRootNamespace(Stream xmlStream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        try
        {
            using var reader = XmlReader.Create(xmlStream, settings);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    return reader.NamespaceURI;
                }
            }
            return null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>Friendly version label for a recognized XTCE namespace, null otherwise.</summary>
    public static string? VersionFor(string? namespaceUri) => namespaceUri switch
    {
        V1_2 => "1.2",
        Legacy => "1.0/1.1",
        _ => null,
    };
}
