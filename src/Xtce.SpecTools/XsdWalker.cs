using System.Xml;
using System.Xml.Linq;

namespace Xtce.SpecTools;

public static class XsdWalker
{
    public static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";

    public static XDocument Load(string path)
    {
        using var reader = XmlReader.Create(path);
        return XDocument.Load(reader, LoadOptions.SetLineInfo);
    }

    public static int LineOf(XElement element) =>
        ((IXmlLineInfo)element).HasLineInfo() ? ((IXmlLineInfo)element).LineNumber : -1;

    /// <summary>
    /// Nearest ancestor (or self) that carries a "name" attribute — used to attribute a
    /// constraint or documentation block to the schema construct that owns it.
    /// </summary>
    public static XElement? NearestNamedAncestor(XElement element)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            if (current.Attribute("name") is { Value.Length: > 0 })
                return current;
        }
        return null;
    }

    public static string OwnerPath(XElement element)
    {
        var names = new List<string>();
        for (var current = element; current is not null; current = current.Parent)
        {
            var name = current.Attribute("name")?.Value;
            if (name is not null)
                names.Add(name);
        }
        names.Reverse();
        return names.Count > 0 ? string.Join("/", names) : "(schema root)";
    }
}
