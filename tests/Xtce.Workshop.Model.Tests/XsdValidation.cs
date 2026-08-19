using System.Xml;
using System.Xml.Schema;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Validates XML against the real XTCE 1.2 schema (reference/1.2/SpaceSystem.xsd). The
/// schema imports the W3C XML-namespace schema from a w3.org URL, which offline CI can't
/// fetch — reference/1.2/xml.xsd is a vendored copy loaded explicitly first, so the import
/// resolves from the schema set instead of the network (XmlResolver stays null).
/// </summary>
internal static class XsdValidation
{
    private static readonly Lazy<XmlSchemaSet> Schemas = new(() =>
    {
        var schemas = new XmlSchemaSet { XmlResolver = null };

        // The vendored xml.xsd carries a DOCTYPE, so its reader needs DTD parsing ignored
        // (never processed — no resolver is set).
        var dtdTolerant = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using (var xmlNamespaceReader = XmlReader.Create(TestPaths.XmlNamespaceSchema, dtdTolerant))
        {
            schemas.Add("http://www.w3.org/XML/1998/namespace", xmlNamespaceReader);
        }
        using (var xtceReader = XmlReader.Create(TestPaths.XtceSchema, dtdTolerant))
        {
            schemas.Add("http://www.omg.org/spec/XTCE/20180204", xtceReader);
        }

        schemas.Compile();
        return schemas;
    });

    /// <summary>Returns all validation errors/warnings for the given XML, empty if valid.</summary>
    public static IReadOnlyList<string> Validate(string xml)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = Schemas.Value,
            XmlResolver = null,
        };
        settings.ValidationEventHandler += (_, e) => errors.Add(e.Message);

        using var reader = XmlReader.Create(new StringReader(xml), settings);
        while (reader.Read())
        {
        }

        return errors;
    }
}
