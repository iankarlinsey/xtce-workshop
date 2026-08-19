using System.Text;
using System.Xml;

namespace Xtce.Workshop.Model;

/// <summary>
/// Writes a SpaceSystem (and its nested Children, recursively) as valid XTCE 1.2 XML —
/// symmetric with XtceDocumentReader. Built on XmlWriter (streaming) rather than
/// XDocument, for the same reason the reader is streaming: consistency, and
/// compatibility with eventually handling tens-of-MB documents without buffering the
/// whole tree as one XML string in memory.
/// </summary>
public static class XtceDocumentWriter
{
    private const string XtceNamespace = "http://www.omg.org/spec/XTCE/20180204";

    /// <summary>
    /// Writes UTF-8 XML to the given stream. Takes a Stream, not a string — writing via
    /// StringWriter makes XmlWriter declare UTF-16 in the XML prolog regardless of how
    /// the resulting string is later encoded to bytes, which breaks round-tripping through
    /// XtceDocumentReader.Load (a mismatched declared-vs-actual encoding with no BOM to
    /// disambiguate). Writing UTF-8 bytes directly avoids that trap entirely.
    /// </summary>
    public static void Write(SpaceSystem spaceSystem, Stream output)
    {
        var settings = new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 };
        using var writer = XmlWriter.Create(output, settings);
        WriteSpaceSystem(writer, spaceSystem);
    }

    public static string Write(SpaceSystem spaceSystem)
    {
        using var stream = new MemoryStream();
        Write(spaceSystem, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSpaceSystem(XmlWriter writer, SpaceSystem spaceSystem)
    {
        // Namespace must be passed explicitly on every element, not just the root —
        // WriteStartElement(localName) without one writes an unqualified (no-namespace)
        // element even when a default namespace is already in scope, which would produce
        // XML that doesn't validate against the XSD (elementFormDefault="qualified").
        writer.WriteStartElement("SpaceSystem", XtceNamespace);
        writer.WriteAttributeString("name", spaceSystem.Name);

        if (spaceSystem.TelemetryMetaData is not null)
        {
            WriteTelemetryMetaData(writer, spaceSystem.TelemetryMetaData);
        }

        foreach (var child in spaceSystem.Children)
        {
            WriteSpaceSystem(writer, child);
        }

        writer.WriteEndElement();
    }

    private static void WriteTelemetryMetaData(XmlWriter writer, TelemetryMetaData telemetryMetaData)
    {
        // TelemetryMetaDataType's sequence orders ParameterTypeSet before ParameterSet — the
        // written order must match the XSD sequence for the document to validate.
        writer.WriteStartElement("TelemetryMetaData", XtceNamespace);

        if (telemetryMetaData.ParameterTypeSet.Count > 0)
        {
            writer.WriteStartElement("ParameterTypeSet", XtceNamespace);
            foreach (var parameterType in telemetryMetaData.ParameterTypeSet)
            {
                WriteParameterType(writer, parameterType);
            }
            writer.WriteEndElement();
        }

        if (telemetryMetaData.ParameterSet.Count > 0)
        {
            writer.WriteStartElement("ParameterSet", XtceNamespace);
            foreach (var parameter in telemetryMetaData.ParameterSet)
            {
                writer.WriteStartElement("Parameter", XtceNamespace);
                writer.WriteAttributeString("name", parameter.Name);
                writer.WriteAttributeString("parameterTypeRef", parameter.ParameterTypeRef);
                if (parameter.InitialValue is not null)
                {
                    writer.WriteAttributeString("initialValue", parameter.InitialValue);
                }
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteParameterType(XmlWriter writer, ParameterTypeDefinition parameterType)
    {
        var elementName = parameterType.Kind switch
        {
            ParameterTypeKind.Integer => "IntegerParameterType",
            ParameterTypeKind.Float => "FloatParameterType",
            ParameterTypeKind.String => "StringParameterType",
            ParameterTypeKind.Boolean => "BooleanParameterType",
            ParameterTypeKind.Enumerated => "EnumeratedParameterType",
            _ => throw new ArgumentOutOfRangeException(
                nameof(parameterType), parameterType.Kind, "Unsupported parameter type kind."),
        };

        writer.WriteStartElement(elementName, XtceNamespace);
        writer.WriteAttributeString("name", parameterType.Name);

        if (parameterType.Kind == ParameterTypeKind.Integer && parameterType.Signed is { } signed)
        {
            writer.WriteAttributeString("signed", XmlConvert.ToString(signed));
        }

        if (parameterType.Kind is ParameterTypeKind.Integer or ParameterTypeKind.Float
            && parameterType.SizeInBits is { } sizeInBits)
        {
            writer.WriteAttributeString("sizeInBits", XmlConvert.ToString(sizeInBits));
        }

        if (parameterType.Kind == ParameterTypeKind.Boolean)
        {
            if (parameterType.OneStringValue is not null)
            {
                writer.WriteAttributeString("oneStringValue", parameterType.OneStringValue);
            }
            if (parameterType.ZeroStringValue is not null)
            {
                writer.WriteAttributeString("zeroStringValue", parameterType.ZeroStringValue);
            }
        }

        if (parameterType.InitialValue is not null)
        {
            writer.WriteAttributeString("initialValue", parameterType.InitialValue);
        }

        if (parameterType.Kind == ParameterTypeKind.Enumerated)
        {
            writer.WriteStartElement("EnumerationList", XtceNamespace);
            foreach (var entry in parameterType.Enumerations ?? Array.Empty<EnumerationEntry>())
            {
                writer.WriteStartElement("Enumeration", XtceNamespace);
                writer.WriteAttributeString("value", XmlConvert.ToString(entry.Value));
                writer.WriteAttributeString("label", entry.Label);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }
}
