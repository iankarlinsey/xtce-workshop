using System.Text;
using System.Xml;

namespace Xtce.Workshop.Model;

/// <summary>
/// Writes a SpaceSystem (and its nested Children, recursively) as valid XTCE 1.2 XML —
/// symmetric with XtceDocumentReader. Built on XmlWriter (streaming) rather than
/// XDocument, for the same reason the reader is streaming: consistency, and
/// compatibility with eventually handling tens-of-MB documents without buffering the
/// whole tree as one XML string in memory.
///
/// Preserved raw fragments (unmodeled elements captured on load — see RawXml.cs, issue #23)
/// are re-emitted via WriteRaw into their XSD-sequence-correct slot among modeled siblings,
/// using per-parent element-order tables and a stable merge, so output written from a
/// schema-valid input stays schema-valid.
/// </summary>
public static class XtceDocumentWriter
{
    private const string XtceNamespace = "http://www.omg.org/spec/XTCE/20180204";

    // XSD sequence order of SpaceSystemType's content (NameDescriptionType's inherited
    // children first, then SpaceSystemType's own sequence).
    private static readonly string[] SpaceSystemChildOrder =
    [
        "LongDescription", "AliasSet", "AncillaryDataSet",
        "Header", "TelemetryMetaData", "CommandMetaData", "ServiceSet", "SpaceSystem",
    ];

    private static readonly string[] TelemetryMetaDataChildOrder =
    [
        "ParameterTypeSet", "ParameterSet", "ContainerSet", "MessageSet", "StreamSet", "AlgorithmSet",
    ];

    // Superset covering every modeled parameter type kind's XSD sequence: DescriptionType
    // children, then BaseDataType (UnitSet, encoding choice), then per-kind extensions —
    // each kind uses a subset of this list in this same relative order.
    private static readonly string[] ParameterTypeChildOrder =
    [
        "LongDescription", "AliasSet", "AncillaryDataSet",
        "UnitSet",
        "BinaryDataEncoding", "FloatDataEncoding", "IntegerDataEncoding", "StringDataEncoding",
        "ToString", "ValidRange", "SizeRangeInCharacters",
        "EnumerationList",
        "DefaultAlarm", "ContextAlarmList", "BinaryContextAlarmList",
    ];

    private static readonly string[] ParameterChildOrder =
    [
        "LongDescription", "AliasSet", "AncillaryDataSet", "ParameterProperties",
    ];

    /// <summary>
    /// Writes UTF-8 XML to the given stream. Takes a Stream, not a string — writing via
    /// StringWriter makes XmlWriter declare UTF-16 in the XML prolog regardless of how
    /// the resulting string is later encoded to bytes, which breaks round-tripping through
    /// XtceDocumentReader.Load (a mismatched declared-vs-actual encoding with no BOM to
    /// disambiguate). Writing UTF-8 bytes directly avoids that trap entirely.
    /// </summary>
    public static void Write(SpaceSystem spaceSystem, Stream output)
    {
        // UTF8Encoding(false), not Encoding.UTF8: the latter emits a BOM, which the string
        // overload then surfaces as a leading U+FEFF character in API responses and breaks
        // strict XML consumers ("data at the root level is invalid").
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
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
        WritePreservedAttributes(writer, spaceSystem.PreservedAttributes);

        var slots = new List<(string Name, Action Emit)>();
        AddPreservedSlots(slots, writer, spaceSystem.Preserved);
        if (spaceSystem.TelemetryMetaData is not null)
        {
            var telemetryMetaData = spaceSystem.TelemetryMetaData;
            slots.Add(("TelemetryMetaData", () => WriteTelemetryMetaData(writer, telemetryMetaData)));
        }
        foreach (var child in spaceSystem.Children)
        {
            var captured = child;
            slots.Add(("SpaceSystem", () => WriteSpaceSystem(writer, captured)));
        }
        EmitInSchemaOrder(SpaceSystemChildOrder, slots);

        writer.WriteEndElement();
    }

    private static void WriteTelemetryMetaData(XmlWriter writer, TelemetryMetaData telemetryMetaData)
    {
        writer.WriteStartElement("TelemetryMetaData", XtceNamespace);

        var slots = new List<(string Name, Action Emit)>();

        if (telemetryMetaData.ParameterTypeSet.Count > 0 || telemetryMetaData.PreservedParameterTypes is { Count: > 0 })
        {
            slots.Add(("ParameterTypeSet", () =>
            {
                writer.WriteStartElement("ParameterTypeSet", XtceNamespace);
                foreach (var parameterType in telemetryMetaData.ParameterTypeSet)
                {
                    WriteParameterType(writer, parameterType);
                }
                // The set is XSD choice-unbounded, so preserved (unmodeled-kind) entries
                // can validly follow the modeled ones regardless of original interleaving.
                WriteFragments(writer, telemetryMetaData.PreservedParameterTypes);
                writer.WriteEndElement();
            }));
        }

        if (telemetryMetaData.ParameterSet.Count > 0 || telemetryMetaData.PreservedParameters is { Count: > 0 })
        {
            slots.Add(("ParameterSet", () =>
            {
                writer.WriteStartElement("ParameterSet", XtceNamespace);
                foreach (var parameter in telemetryMetaData.ParameterSet)
                {
                    WriteParameter(writer, parameter);
                }
                WriteFragments(writer, telemetryMetaData.PreservedParameters);
                writer.WriteEndElement();
            }));
        }

        AddPreservedSlots(slots, writer, telemetryMetaData.Preserved);
        EmitInSchemaOrder(TelemetryMetaDataChildOrder, slots);

        writer.WriteEndElement();
    }

    private static void WriteParameter(XmlWriter writer, Parameter parameter)
    {
        writer.WriteStartElement("Parameter", XtceNamespace);
        writer.WriteAttributeString("name", parameter.Name);
        writer.WriteAttributeString("parameterTypeRef", parameter.ParameterTypeRef);
        if (parameter.InitialValue is not null)
        {
            writer.WriteAttributeString("initialValue", parameter.InitialValue);
        }
        WritePreservedAttributes(writer, parameter.PreservedAttributes);

        var slots = new List<(string Name, Action Emit)>();
        AddPreservedSlots(slots, writer, parameter.Preserved);
        EmitInSchemaOrder(ParameterChildOrder, slots);

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

        WritePreservedAttributes(writer, parameterType.PreservedAttributes);

        var slots = new List<(string Name, Action Emit)>();
        AddPreservedSlots(slots, writer, parameterType.Preserved);
        if (parameterType.Kind == ParameterTypeKind.Enumerated)
        {
            slots.Add(("EnumerationList", () =>
            {
                writer.WriteStartElement("EnumerationList", XtceNamespace);
                foreach (var entry in parameterType.Enumerations ?? Array.Empty<EnumerationEntry>())
                {
                    writer.WriteStartElement("Enumeration", XtceNamespace);
                    writer.WriteAttributeString("value", XmlConvert.ToString(entry.Value));
                    if (entry.MaxValue is { } maxValue)
                    {
                        writer.WriteAttributeString("maxValue", XmlConvert.ToString(maxValue));
                    }
                    writer.WriteAttributeString("label", entry.Label);
                    if (entry.ShortDescription is not null)
                    {
                        writer.WriteAttributeString("shortDescription", entry.ShortDescription);
                    }
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }));
        }
        EmitInSchemaOrder(ParameterTypeChildOrder, slots);

        writer.WriteEndElement();
    }

    private static void AddPreservedSlots(
        List<(string Name, Action Emit)> slots, XmlWriter writer, IReadOnlyList<RawXmlFragment>? preserved)
    {
        if (preserved is null)
        {
            return;
        }
        foreach (var fragment in preserved)
        {
            var captured = fragment;
            slots.Add((fragment.ElementName, () => writer.WriteRaw(captured.OuterXml)));
        }
    }

    private static void WriteFragments(XmlWriter writer, IReadOnlyList<RawXmlFragment>? fragments)
    {
        if (fragments is null)
        {
            return;
        }
        foreach (var fragment in fragments)
        {
            writer.WriteRaw(fragment.OuterXml);
        }
    }

    /// <summary>
    /// Emits slot actions stably sorted by their element name's position in the parent's
    /// XSD sequence table — preserved fragments interleave correctly with modeled elements,
    /// and same-named entries keep their captured relative order (OrderBy is stable). A name
    /// missing from the table (shouldn't happen for schema-valid input) sorts last rather
    /// than throwing: emitting it somewhere beats losing it.
    /// </summary>
    private static void EmitInSchemaOrder(string[] orderTable, List<(string Name, Action Emit)> slots)
    {
        foreach (var slot in slots.OrderBy(s =>
                 {
                     var index = Array.IndexOf(orderTable, s.Name);
                     return index < 0 ? orderTable.Length : index;
                 }))
        {
            slot.Emit();
        }
    }

    private static void WritePreservedAttributes(XmlWriter writer, IReadOnlyList<RawAttribute>? attributes)
    {
        if (attributes is null)
        {
            return;
        }

        foreach (var attribute in attributes)
        {
            var colon = attribute.Name.IndexOf(':');
            if (colon < 0)
            {
                writer.WriteAttributeString(attribute.Name, attribute.Value);
            }
            else
            {
                // Prefixed attribute (xsi:schemaLocation, xml:base) or prefixed namespace
                // declaration (xmlns:xsi) — re-bind the original prefix to its captured
                // namespace so preserved fragments that use the prefix stay resolvable.
                var prefix = attribute.Name[..colon];
                var localName = attribute.Name[(colon + 1)..];
                writer.WriteAttributeString(prefix, localName, attribute.NamespaceUri, attribute.Value);
            }
        }
    }
}
