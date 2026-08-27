using System.Reflection;
using System.Xml;
using System.Xml.Schema;

namespace Xtce.Workshop.Validation;

/// <summary>
/// Validates document XML against the real XTCE 1.2 schema, loaded from resources embedded
/// in this assembly (SpaceSystem.xsd plus the vendored W3C xml.xsd its import needs), so
/// the API container and the CLI can schema-validate without a repo checkout or network
/// access.
/// </summary>
public static class SchemaValidator
{
    private static readonly Lazy<XmlSchemaSet> Schemas = new(() =>
    {
        var assembly = typeof(SchemaValidator).Assembly;
        var schemas = new XmlSchemaSet { XmlResolver = null };
        var dtdTolerant = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };

        using (var xmlXsd = assembly.GetManifestResourceStream("xml.xsd")
            ?? throw new InvalidOperationException("Embedded xml.xsd missing."))
        using (var reader = XmlReader.Create(xmlXsd, dtdTolerant))
        {
            schemas.Add("http://www.w3.org/XML/1998/namespace", reader);
        }
        using (var xtceXsd = assembly.GetManifestResourceStream("SpaceSystem.xsd")
            ?? throw new InvalidOperationException("Embedded SpaceSystem.xsd missing."))
        using (var reader = XmlReader.Create(xtceXsd, dtdTolerant))
        {
            schemas.Add("http://www.omg.org/spec/XTCE/20180204", reader);
        }

        schemas.Compile();
        return schemas;
    });

    /// <summary>All schema validation errors/warnings for the given XML; empty = valid.</summary>
    public static IReadOnlyList<string> Validate(string xml) =>
        ValidateDetailed(xml).Select(error => error.Message).ToList();

    /// <summary>Schema errors with their source positions (null when the parser had none).</summary>
    public static IReadOnlyList<SchemaError> ValidateDetailed(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), CreateSettings(out var errors));
        return Drain(reader, errors);
    }

    /// <summary>Stream overload — lets callers wrap the input for progress/cancellation.</summary>
    public static IReadOnlyList<SchemaError> ValidateDetailed(Stream xml)
    {
        using var reader = XmlReader.Create(xml, CreateSettings(out var errors));
        return Drain(reader, errors);
    }

    private static XmlReaderSettings CreateSettings(out List<SchemaError> errors)
    {
        var captured = new List<SchemaError>();
        errors = captured;
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = Schemas.Value,
            XmlResolver = null,
        };
        settings.ValidationEventHandler += (_, e) => captured.Add(new SchemaError(
            e.Message,
            e.Exception?.LineNumber > 0 ? e.Exception.LineNumber : null,
            e.Exception?.LinePosition > 0 ? e.Exception.LinePosition : null));
        return settings;
    }

    private static IReadOnlyList<SchemaError> Drain(XmlReader reader, List<SchemaError> errors)
    {
        try
        {
            while (reader.Read())
            {
            }
        }
        catch (XmlException ex)
        {
            errors.Add(new SchemaError(
                $"Not well-formed: {ex.Message}",
                ex.LineNumber > 0 ? ex.LineNumber : null,
                ex.LinePosition > 0 ? ex.LinePosition : null));
        }

        return errors;
    }
}

/// <summary>One XSD validation error, positioned when the validator reported a position.</summary>
public sealed record SchemaError(string Message, int? Line, int? Column);
