using System.Xml;

namespace Xtce.Workshop.Model;

/// <summary>
/// Reads a SpaceSystem element (and, recursively, its nested SpaceSystem children) from
/// an XTCE document. Deliberately built on XmlReader (forward-only, non-buffering)
/// rather than XDocument/XmlDocument — XTCE files can be tens of megabytes, and the
/// reading strategy needs to survive being extended to a real streaming parser later
/// without a rewrite. See another implementation's streaming reader for the pattern this follows.
///
/// Anything the object model doesn't represent is PRESERVED, not dropped (issue #23):
/// unmodeled child elements are captured verbatim via ReadOuterXml into RawXmlFragment
/// lists, and unmodeled attributes into RawAttribute lists, so XtceDocumentWriter can
/// write them back and a load → save round trip never loses data the editor didn't touch.
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

        var preservedAttributes = CapturePreservedAttributes(reader, "name");

        var children = new List<SpaceSystem>();
        TelemetryMetaData? telemetryMetaData = null;
        List<RawXmlFragment>? preserved = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return new SpaceSystem(name, children, telemetryMetaData, preserved, preservedAttributes);
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
                // Unmodeled child (LongDescription, AliasSet, AncillaryDataSet, Header,
                // CommandMetaData, ServiceSet) — preserved verbatim, re-emitted on save.
                Preserve(ref preserved, reader);
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();

        return new SpaceSystem(name, children, telemetryMetaData, preserved, preservedAttributes);
    }

    private static TelemetryMetaData ReadTelemetryMetaData(XmlReader reader)
    {
        var parameterTypes = new List<ParameterTypeDefinition>();
        var parameters = new List<Parameter>();
        List<RawXmlFragment>? preservedTypes = null;
        List<RawXmlFragment>? preservedParameters = null;
        List<RawXmlFragment>? preserved = null;

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
                ReadParameterTypeSet(reader, parameterTypes, ref preservedTypes);
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ParameterSet")
            {
                ReadParameterSet(reader, parameters, ref preservedParameters);
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // Unmodeled sibling (ContainerSet, MessageSet, StreamSet, AlgorithmSet) —
                // preserved verbatim, re-emitted in XSD sequence order on save.
                Preserve(ref preserved, reader);
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();

        return new TelemetryMetaData(parameterTypes, parameters, preservedTypes, preservedParameters, preserved);
    }

    private static void ReadParameterTypeSet(
        XmlReader reader,
        List<ParameterTypeDefinition> parameterTypes,
        ref List<RawXmlFragment>? preservedTypes)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
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
                // Unmodeled parameter type kind (Binary, RelativeTime, AbsoluteTime, Array,
                // Aggregate) — preserved verbatim. The set is XSD choice-unbounded, so
                // re-emitting these after the modeled entries stays schema-valid.
                Preserve(ref preservedTypes, reader);
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();
    }

    private static ParameterTypeDefinition ReadParameterTypeDefinition(XmlReader reader, ParameterTypeKind kind)
    {
        var name = RequireAttribute(reader, "name", "a parameter type");
        var initialValue = reader.GetAttribute("initialValue");

        // Absent modeled attributes stay null — XSD defaults (signed=true, sizeInBits=32,
        // oneStringValue="True"...) are applied by validators at check time, never baked in
        // here, so an attribute the author omitted stays omitted on save.
        bool? signed = kind == ParameterTypeKind.Integer
            ? ParseBool(reader, "signed")
            : null;
        long? sizeInBits = kind is ParameterTypeKind.Integer or ParameterTypeKind.Float
            ? ParseLong(reader, "sizeInBits")
            : null;
        var oneStringValue = kind == ParameterTypeKind.Boolean ? reader.GetAttribute("oneStringValue") : null;
        var zeroStringValue = kind == ParameterTypeKind.Boolean ? reader.GetAttribute("zeroStringValue") : null;
        List<EnumerationEntry>? enumerations = kind == ParameterTypeKind.Enumerated ? new List<EnumerationEntry>() : null;

        var modeledAttributes = kind switch
        {
            ParameterTypeKind.Integer => new[] { "name", "initialValue", "signed", "sizeInBits" },
            ParameterTypeKind.Float => new[] { "name", "initialValue", "sizeInBits" },
            ParameterTypeKind.Boolean => new[] { "name", "initialValue", "oneStringValue", "zeroStringValue" },
            _ => new[] { "name", "initialValue" },
        };
        var preservedAttributes = CapturePreservedAttributes(reader, modeledAttributes);

        List<RawXmlFragment>? preserved = null;

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
                    // ValidRange, SizeRangeInCharacters, LongDescription, AliasSet, ... —
                    // none modeled yet; preserved verbatim.
                    Preserve(ref preserved, reader);
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
            enumerations,
            preserved,
            preservedAttributes);
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
                var maxValue = ParseLong(reader, "maxValue");
                var shortDescription = reader.GetAttribute("shortDescription");
                entries.Add(new EnumerationEntry(parsedValue, label, maxValue, shortDescription));
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

    private static void ReadParameterSet(
        XmlReader reader,
        List<Parameter> parameters,
        ref List<RawXmlFragment>? preservedParameters)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Parameter")
            {
                parameters.Add(ReadParameter(reader));
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // ParameterRef (cross-subsystem parameter includes) — preserved verbatim.
                Preserve(ref preservedParameters, reader);
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();
    }

    private static Parameter ReadParameter(XmlReader reader)
    {
        var name = RequireAttribute(reader, "name", "a Parameter");
        var parameterTypeRef = RequireAttribute(reader, "parameterTypeRef", "a Parameter");
        var initialValue = reader.GetAttribute("initialValue");
        var preservedAttributes = CapturePreservedAttributes(reader, "name", "parameterTypeRef", "initialValue");

        List<RawXmlFragment>? preserved = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    // ParameterProperties, LongDescription, AliasSet, AncillaryDataSet —
                    // preserved verbatim.
                    Preserve(ref preserved, reader);
                }
                else
                {
                    reader.Read();
                }
            }

            reader.ReadEndElement();
        }

        return new Parameter(name, parameterTypeRef, initialValue, preserved, preservedAttributes);
    }

    /// <summary>
    /// Captures the element the reader is positioned on as a verbatim fragment and advances
    /// past it — the preservation replacement for reader.Skip().
    /// </summary>
    private static void Preserve(ref List<RawXmlFragment>? preserved, XmlReader reader)
    {
        var elementName = reader.LocalName;
        var outerXml = reader.ReadOuterXml();
        (preserved ??= new List<RawXmlFragment>()).Add(new RawXmlFragment(elementName, outerXml));
    }

    /// <summary>
    /// Captures every attribute on the current element that isn't in the modeled list —
    /// including prefixed attributes (xsi:schemaLocation, xml:base) and prefixed namespace
    /// declarations (xmlns:xsi), whose prefixes must survive so preserved fragments that use
    /// them stay resolvable. The default xmlns declaration is the one exception: the writer
    /// re-derives it from the XTCE namespace it always emits.
    /// </summary>
    private static IReadOnlyList<RawAttribute>? CapturePreservedAttributes(XmlReader reader, params string[] modeledNames)
    {
        List<RawAttribute>? captured = null;

        if (reader.MoveToFirstAttribute())
        {
            do
            {
                if (reader.Name == "xmlns")
                {
                    continue;
                }
                if (reader.Prefix.Length == 0 && Array.IndexOf(modeledNames, reader.LocalName) >= 0)
                {
                    continue;
                }

                (captured ??= new List<RawAttribute>()).Add(new RawAttribute(
                    reader.Name,
                    reader.Value,
                    reader.NamespaceURI.Length == 0 ? null : reader.NamespaceURI));
            } while (reader.MoveToNextAttribute());

            reader.MoveToElement();
        }

        return captured;
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

    // Unparseable modeled attributes are a hard error, not a silent null — nulling them
    // would drop the attribute on save, silently altering the document (issue #23).
    private static bool? ParseBool(XmlReader reader, string attributeName)
    {
        var value = reader.GetAttribute(attributeName);
        if (value is null)
        {
            return null;
        }
        if (!bool.TryParse(value, out var parsed))
        {
            throw new XtceParseException($"Attribute '{attributeName}' value '{value}' is not a valid boolean.");
        }
        return parsed;
    }

    private static long? ParseLong(XmlReader reader, string attributeName)
    {
        var value = reader.GetAttribute(attributeName);
        if (value is null)
        {
            return null;
        }
        if (!long.TryParse(value, out var parsed))
        {
            throw new XtceParseException($"Attribute '{attributeName}' value '{value}' is not a valid integer.");
        }
        return parsed;
    }
}
