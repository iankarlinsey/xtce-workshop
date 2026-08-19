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

    private static readonly IReadOnlyDictionary<string, ParameterTypeKind> ParameterTypeElementKinds =
        new Dictionary<string, ParameterTypeKind>
        {
            ["IntegerParameterType"] = ParameterTypeKind.Integer,
            ["FloatParameterType"] = ParameterTypeKind.Float,
            ["StringParameterType"] = ParameterTypeKind.String,
            ["BooleanParameterType"] = ParameterTypeKind.Boolean,
            ["EnumeratedParameterType"] = ParameterTypeKind.Enumerated,
        };

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
        TelemetryMetaData? telemetryMetaData = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return new SpaceSystem(name, children, telemetryMetaData);
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == ExpectedRootElementName)
            {
                children.Add(ReadSpaceSystem(reader));
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "TelemetryMetaData")
            {
                telemetryMetaData = ReadTelemetryMetaData(reader);
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // Not-yet-supported child (Header, CommandMetaData, ...) — skip its whole
                // subtree without buffering it.
                reader.Skip();
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();

        return new SpaceSystem(name, children, telemetryMetaData);
    }

    private static TelemetryMetaData ReadTelemetryMetaData(XmlReader reader)
    {
        var parameterTypes = new List<ParameterTypeDefinition>();
        var parameters = new List<Parameter>();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return new TelemetryMetaData(parameterTypes, parameters);
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ParameterTypeSet")
            {
                parameterTypes.AddRange(ReadParameterTypeSet(reader));
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ParameterSet")
            {
                parameters.AddRange(ReadParameterSet(reader));
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // Not-yet-supported sibling (ContainerSet, MessageSet, StreamSet,
                // AlgorithmSet) — skipped, same pattern as unsupported SpaceSystem children.
                reader.Skip();
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();

        return new TelemetryMetaData(parameterTypes, parameters);
    }

    private static List<ParameterTypeDefinition> ReadParameterTypeSet(XmlReader reader)
    {
        var parameterTypes = new List<ParameterTypeDefinition>();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return parameterTypes;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element &&
                ParameterTypeElementKinds.TryGetValue(reader.LocalName, out var kind))
            {
                parameterTypes.Add(ReadParameterTypeDefinition(reader, kind));
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // Not-yet-supported parameter type kind (Binary, RelativeTime, AbsoluteTime,
                // Array, Aggregate) — skipped, not lossily represented. See issue #21.
                reader.Skip();
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();

        return parameterTypes;
    }

    private static ParameterTypeDefinition ReadParameterTypeDefinition(XmlReader reader, ParameterTypeKind kind)
    {
        var name = RequireAttribute(reader, "name", "a parameter type");
        var initialValue = reader.GetAttribute("initialValue");
        bool? signed = kind == ParameterTypeKind.Integer ? ParseBool(reader.GetAttribute("signed")) ?? true : null;
        long? sizeInBits = kind is ParameterTypeKind.Integer or ParameterTypeKind.Float
            ? ParseLong(reader.GetAttribute("sizeInBits"))
            : null;
        var oneStringValue = kind == ParameterTypeKind.Boolean ? reader.GetAttribute("oneStringValue") ?? "True" : null;
        var zeroStringValue = kind == ParameterTypeKind.Boolean ? reader.GetAttribute("zeroStringValue") ?? "False" : null;
        List<EnumerationEntry>? enumerations = kind == ParameterTypeKind.Enumerated ? new List<EnumerationEntry>() : null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "EnumerationList" && enumerations is not null)
                {
                    enumerations.AddRange(ReadEnumerationList(reader));
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // UnitSet, data-encoding choice, DefaultAlarm, ContextAlarmList, ToString,
                    // ValidRange, SizeRangeInCharacters — none modeled in this slice.
                    reader.Skip();
                }
                else
                {
                    reader.Read();
                }
            }

            reader.ReadEndElement();
        }

        return new ParameterTypeDefinition(
            name,
            kind,
            initialValue,
            signed,
            sizeInBits,
            oneStringValue,
            zeroStringValue,
            enumerations);
    }

    private static List<EnumerationEntry> ReadEnumerationList(XmlReader reader)
    {
        var entries = new List<EnumerationEntry>();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return entries;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Enumeration")
            {
                var value = RequireAttribute(reader, "value", "an Enumeration");
                var label = RequireAttribute(reader, "label", "an Enumeration");
                if (!long.TryParse(value, out var parsedValue))
                {
                    throw new XtceParseException($"Enumeration value '{value}' is not a valid integer.");
                }
                entries.Add(new EnumerationEntry(parsedValue, label));
                reader.Skip();
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                reader.Skip();
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();

        return entries;
    }

    private static List<Parameter> ReadParameterSet(XmlReader reader)
    {
        var parameters = new List<Parameter>();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return parameters;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Parameter")
            {
                var name = RequireAttribute(reader, "name", "a Parameter");
                var parameterTypeRef = RequireAttribute(reader, "parameterTypeRef", "a Parameter");
                var initialValue = reader.GetAttribute("initialValue");
                parameters.Add(new Parameter(name, parameterTypeRef, initialValue));
                reader.Skip();
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // ParameterRef (cross-subsystem parameter includes) — out of scope for this
                // slice (see issue #21), skipped rather than lossily represented.
                reader.Skip();
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();

        return parameters;
    }

    private static string RequireAttribute(XmlReader reader, string attributeName, string elementDescription)
    {
        var value = reader.GetAttribute(attributeName);
        if (string.IsNullOrEmpty(value))
        {
            throw new XtceParseException($"{elementDescription} element is missing its required '{attributeName}' attribute.");
        }
        return value;
    }

    private static bool? ParseBool(string? value) =>
        value is null ? null : bool.TryParse(value, out var parsed) ? parsed : null;

    private static long? ParseLong(string? value) =>
        value is null ? null : long.TryParse(value, out var parsed) ? parsed : null;
}
