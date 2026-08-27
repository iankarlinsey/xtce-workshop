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
/// Preserved raw fragments (unmodeled elements captured on load — see RawXml.cs)
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
    // children, then BaseDataType (UnitSet, encoding choice) or BaseTimeDataType (Encoding,
    // ReferenceTime), then per-kind extensions — each kind uses a subset of this list in
    // this same relative order (the BaseDataType and BaseTimeDataType families never mix
    // children, so one shared table is safe).
    private static readonly string[] ParameterTypeChildOrder =
    [
        "LongDescription", "AliasSet", "AncillaryDataSet",
        "DimensionList", "MemberList",
        "Encoding", "ReferenceTime",
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

    // SequenceContainerType: DescriptionType children, then ContainerType's
    // (DefaultRateInStream, RateInStreamSet, BinaryEncoding), then its own sequence.
    private static readonly string[] SequenceContainerChildOrder =
    [
        "LongDescription", "AliasSet", "AncillaryDataSet",
        "DefaultRateInStream", "RateInStreamSet", "BinaryEncoding",
        "EntryList", "BaseContainer",
    ];

    // SequenceEntryType's sequence — preserved children of a modeled ref entry.
    private static readonly string[] SequenceEntryChildOrder =
    [
        "LocationInContainerInBits", "RepeatEntry", "IncludeCondition", "TimeAssociation", "AncillaryDataSet",
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
        // Leading comments precede the start tag: for the root these are the document
        // prolog (license headers); for nested systems, comments that sat before them.
        WriteLeadingComments(writer, spaceSystem.Preserved);

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
        if (spaceSystem.CommandMetaData is not null)
        {
            var commandMetaData = spaceSystem.CommandMetaData;
            slots.Add(("CommandMetaData", () => WriteCommandMetaData(writer, commandMetaData)));
        }
        foreach (var child in spaceSystem.Children)
        {
            var captured = child;
            slots.Add(("SpaceSystem", () => WriteSpaceSystem(writer, captured)));
        }
        EmitInSchemaOrder(SpaceSystemChildOrder, slots);

        writer.WriteEndElement();

        // Trailing comments follow the end tag: document epilog for the root.
        WriteTrailingComments(writer, spaceSystem.Preserved);
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

        if (telemetryMetaData.ContainerSet is { Count: > 0 } || telemetryMetaData.PreservedContainerEntries is { Count: > 0 })
        {
            slots.Add(("ContainerSet", () =>
            {
                writer.WriteStartElement("ContainerSet", XtceNamespace);
                foreach (var container in telemetryMetaData.ContainerSet ?? [])
                {
                    WriteSequenceContainer(writer, container);
                }
                // Quarantined (unparseable) containers ride verbatim at the set's end —
                // the set is choice-unbounded, so placement stays schema-shaped.
                WriteFragments(writer, telemetryMetaData.PreservedContainerEntries);
                writer.WriteEndElement();
            }));
        }

        if (telemetryMetaData.MessageSet is { } messageSet)
        {
            slots.Add(("MessageSet", () => WriteMessageSet(writer, messageSet)));
        }

        AddPreservedSlots(slots, writer, telemetryMetaData.Preserved);
        EmitInSchemaOrder(TelemetryMetaDataChildOrder, slots);

        writer.WriteEndElement();
    }

    // MessageType's sequence: DescriptionType children, then MatchCriteria, then ContainerRef.
    private static readonly string[] MessageChildOrder =
    [
        "LongDescription", "AliasSet", "AncillaryDataSet", "MatchCriteria", "ContainerRef",
    ];

    private static readonly string[] MessageSetChildOrder =
    [
        "LongDescription", "AliasSet", "AncillaryDataSet", "Message",
    ];

    private static void WriteMessageSet(XmlWriter writer, MessageSet messageSet)
    {
        writer.WriteStartElement("MessageSet", XtceNamespace);
        WritePreservedAttributes(writer, messageSet.PreservedAttributes);

        var slots = new List<(string Name, Action Emit)>();
        AddPreservedSlots(slots, writer, messageSet.Preserved);
        foreach (var message in messageSet.Messages)
        {
            var captured = message;
            slots.Add(("Message", () =>
            {
                WriteLeadingComments(writer, captured.Preserved);
                writer.WriteStartElement("Message", XtceNamespace);
                writer.WriteAttributeString("name", captured.Name);
                WritePreservedAttributes(writer, captured.PreservedAttributes);

                var messageSlots = new List<(string Name, Action Emit)>();
                AddPreservedSlots(messageSlots, writer, captured.Preserved);
                messageSlots.Add(("ContainerRef", () =>
                {
                    writer.WriteStartElement("ContainerRef", XtceNamespace);
                    writer.WriteAttributeString("containerRef", captured.ContainerRef);
                    writer.WriteEndElement();
                }));
                EmitInSchemaOrder(MessageChildOrder, messageSlots);

                writer.WriteEndElement();
            }));
        }
        EmitInSchemaOrder(MessageSetChildOrder, slots);

        writer.WriteEndElement();
    }

    private static readonly string[] CommandMetaDataChildOrder =
    [
        "ParameterTypeSet", "ParameterSet", "ArgumentTypeSet", "MetaCommandSet",
        "CommandContainerSet", "StreamSet", "AlgorithmSet",
    ];

    // MetaCommandType's full sequence (DescriptionType children first).
    private static readonly string[] MetaCommandChildOrder =
    [
        "LongDescription", "AliasSet", "AncillaryDataSet",
        "BaseMetaCommand", "SystemName", "ArgumentList", "CommandContainer",
        "TransmissionConstraintList", "DefaultSignificance", "ContextSignificanceList",
        "Interlock", "VerifierSet", "ParameterToSetList", "ParametersToSuspendAlarmsOnSet",
    ];

    private static readonly string[] VerifierSetChildOrder =
    [
        "TransferredToRangeVerifier", "SentFromRangeVerifier", "ReceivedVerifier",
        "AcceptedVerifier", "QueuedVerifier", "ExecutionVerifier", "CompleteVerifier", "FailedVerifier",
    ];

    private static void WriteCommandMetaData(XmlWriter writer, CommandMetaData commandMetaData)
    {
        writer.WriteStartElement("CommandMetaData", XtceNamespace);

        var slots = new List<(string Name, Action Emit)>();
        AddPreservedSlots(slots, writer, commandMetaData.Preserved);

        if (commandMetaData.MetaCommands.Count > 0 || commandMetaData.PreservedEntries is { Count: > 0 })
        {
            slots.Add(("MetaCommandSet", () =>
            {
                writer.WriteStartElement("MetaCommandSet", XtceNamespace);
                foreach (var metaCommand in commandMetaData.MetaCommands)
                {
                    WriteMetaCommand(writer, metaCommand);
                }
                WriteFragments(writer, commandMetaData.PreservedEntries);
                writer.WriteEndElement();
            }));
        }

        EmitInSchemaOrder(CommandMetaDataChildOrder, slots);

        writer.WriteEndElement();
    }

    private static void WriteMetaCommand(XmlWriter writer, MetaCommand metaCommand)
    {
        WriteLeadingComments(writer, metaCommand.Preserved);
        writer.WriteStartElement("MetaCommand", XtceNamespace);
        writer.WriteAttributeString("name", metaCommand.Name);
        if (metaCommand.Abstract is { } isAbstract)
        {
            writer.WriteAttributeString("abstract", XmlConvert.ToString(isAbstract));
        }
        WritePreservedAttributes(writer, metaCommand.PreservedAttributes);

        var slots = new List<(string Name, Action Emit)>();
        AddPreservedSlots(slots, writer, metaCommand.Preserved);

        if (metaCommand.BaseMetaCommandRef is { } baseRef)
        {
            slots.Add(("BaseMetaCommand", () =>
            {
                writer.WriteStartElement("BaseMetaCommand", XtceNamespace);
                writer.WriteAttributeString("metaCommandRef", baseRef);
                WriteFragments(writer, metaCommand.BaseMetaCommandPreserved);
                writer.WriteEndElement();
            }));
        }

        if (metaCommand.CommandContainer is { } commandContainer)
        {
            slots.Add(("CommandContainer", () =>
            {
                writer.WriteStartElement("CommandContainer", XtceNamespace);
                writer.WriteAttributeString("name", commandContainer.Name);
                WritePreservedAttributes(writer, commandContainer.PreservedAttributes);

                var containerSlots = new List<(string Name, Action Emit)>();
                AddPreservedSlots(containerSlots, writer, commandContainer.Preserved);
                if (commandContainer.BaseContainerRef is { } baseRef)
                {
                    containerSlots.Add(("BaseContainer", () =>
                    {
                        writer.WriteStartElement("BaseContainer", XtceNamespace);
                        writer.WriteAttributeString("containerRef", baseRef);
                        WriteFragments(writer, commandContainer.BaseContainerPreserved);
                        writer.WriteEndElement();
                    }));
                }
                EmitInSchemaOrder(SequenceContainerChildOrder, containerSlots);

                writer.WriteEndElement();
            }));
        }

        var hasVerifiers = metaCommand.ExecutionVerifiers is { Count: > 0 }
            || metaCommand.CompleteVerifiers is { Count: > 0 }
            || metaCommand.PreservedVerifiers is { Count: > 0 };
        if (hasVerifiers)
        {
            slots.Add(("VerifierSet", () =>
            {
                writer.WriteStartElement("VerifierSet", XtceNamespace);
                var verifierSlots = new List<(string Name, Action Emit)>();
                AddPreservedSlots(verifierSlots, writer, metaCommand.PreservedVerifiers);
                AddPreservedSlots(verifierSlots, writer, metaCommand.ExecutionVerifiers);
                AddPreservedSlots(verifierSlots, writer, metaCommand.CompleteVerifiers);
                EmitInSchemaOrder(VerifierSetChildOrder, verifierSlots);
                writer.WriteEndElement();
            }));
        }

        EmitInSchemaOrder(MetaCommandChildOrder, slots);

        writer.WriteEndElement();
    }

    private static void WriteSequenceContainer(XmlWriter writer, SequenceContainer container)
    {
        WriteLeadingComments(writer, container.Preserved);
        writer.WriteStartElement("SequenceContainer", XtceNamespace);
        writer.WriteAttributeString("name", container.Name);
        if (container.Abstract is { } isAbstract)
        {
            writer.WriteAttributeString("abstract", XmlConvert.ToString(isAbstract));
        }
        WritePreservedAttributes(writer, container.PreservedAttributes);

        var slots = new List<(string Name, Action Emit)>();
        AddPreservedSlots(slots, writer, container.Preserved);

        // EntryList is required by the XSD (minOccurs defaults to 1) — always written,
        // even when empty.
        slots.Add(("EntryList", () =>
        {
            writer.WriteStartElement("EntryList", XtceNamespace);
            foreach (var entry in container.EntryList)
            {
                WriteSequenceEntry(writer, entry);
            }
            writer.WriteEndElement();
        }));

        if (container.BaseContainer is { } baseContainer)
        {
            slots.Add(("BaseContainer", () => WriteBaseContainer(writer, baseContainer)));
        }

        EmitInSchemaOrder(SequenceContainerChildOrder, slots);

        writer.WriteEndElement();

        // ContainerSet has no preserved list of its own, so comments trailing the set ride
        // on its last container and are re-emitted here, after the container's end tag.
        WriteTrailingComments(writer, container.Preserved);
    }

    private static void WriteSequenceEntry(XmlWriter writer, SequenceEntry entry)
    {
        if (entry.Kind == SequenceEntryKind.Raw)
        {
            var rawXml = entry.RawXml
                ?? throw new InvalidOperationException("A Raw sequence entry has no RawXml payload.");
            if (rawXml.ElementName == CommentAnchor.ElementName)
            {
                WriteCommentText(writer, rawXml.OuterXml); // a comment kept in entry-list position
            }
            else
            {
                WriteFragmentXml(writer, rawXml.OuterXml);
            }
            return;
        }

        var (elementName, refAttributeName) = entry.Kind switch
        {
            SequenceEntryKind.ParameterRef => ("ParameterRefEntry", "parameterRef"),
            SequenceEntryKind.ContainerRef => ("ContainerRefEntry", "containerRef"),
            _ => throw new ArgumentOutOfRangeException(nameof(entry), entry.Kind, "Unsupported entry kind."),
        };

        writer.WriteStartElement(elementName, XtceNamespace);
        writer.WriteAttributeString(refAttributeName, entry.Ref
            ?? throw new InvalidOperationException($"A {elementName} entry has no Ref."));
        WritePreservedAttributes(writer, entry.PreservedAttributes);

        var slots = new List<(string Name, Action Emit)>();
        AddPreservedSlots(slots, writer, entry.Preserved);
        EmitInSchemaOrder(SequenceEntryChildOrder, slots);

        writer.WriteEndElement();
    }

    private static void WriteBaseContainer(XmlWriter writer, BaseContainer baseContainer)
    {
        writer.WriteStartElement("BaseContainer", XtceNamespace);
        writer.WriteAttributeString("containerRef", baseContainer.ContainerRef);

        if (baseContainer.RestrictionCriteria is { } criteria)
        {
            writer.WriteStartElement("RestrictionCriteria", XtceNamespace);

            // The match-criteria choice comes first (it's the MatchCriteriaType base
            // particle); NextContainer, when present, is ADDITIVE and must follow it —
            // see RestrictionCriteria's doc comment for the schema subtlety.
            if (criteria.Comparison is { } comparison)
            {
                WriteComparison(writer, comparison);
            }
            else if (criteria.ComparisonList is { } comparisonList)
            {
                writer.WriteStartElement("ComparisonList", XtceNamespace);
                foreach (var item in comparisonList)
                {
                    WriteComparison(writer, item);
                }
                writer.WriteEndElement();
            }
            else if (criteria.Raw is { } raw)
            {
                WriteFragmentXml(writer, raw.OuterXml);
            }

            if (criteria.NextContainerRef is { } nextContainerRef)
            {
                writer.WriteStartElement("NextContainer", XtceNamespace);
                writer.WriteAttributeString("containerRef", nextContainerRef);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteComparison(XmlWriter writer, Comparison comparison)
    {
        writer.WriteStartElement("Comparison", XtceNamespace);
        writer.WriteAttributeString("parameterRef", comparison.ParameterRef);
        if (comparison.Instance is { } instance)
        {
            writer.WriteAttributeString("instance", XmlConvert.ToString(instance));
        }
        if (comparison.UseCalibratedValue is { } useCalibratedValue)
        {
            writer.WriteAttributeString("useCalibratedValue", XmlConvert.ToString(useCalibratedValue));
        }
        if (comparison.ComparisonOperator is { } comparisonOperator)
        {
            writer.WriteAttributeString("comparisonOperator", comparisonOperator);
        }
        writer.WriteAttributeString("value", comparison.Value);
        WritePreservedAttributes(writer, comparison.PreservedAttributes);
        writer.WriteEndElement();
    }

    private static void WriteParameter(XmlWriter writer, Parameter parameter)
    {
        WriteLeadingComments(writer, parameter.Preserved);
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
            ParameterTypeKind.Binary => "BinaryParameterType",
            ParameterTypeKind.RelativeTime => "RelativeTimeParameterType",
            ParameterTypeKind.AbsoluteTime => "AbsoluteTimeParameterType",
            ParameterTypeKind.Array => "ArrayParameterType",
            ParameterTypeKind.Aggregate => "AggregateParameterType",
            _ => throw new ArgumentOutOfRangeException(
                nameof(parameterType), parameterType.Kind, "Unsupported parameter type kind."),
        };

        WriteLeadingComments(writer, parameterType.Preserved);
        writer.WriteStartElement(elementName, XtceNamespace);
        writer.WriteAttributeString("name", parameterType.Name);

        if (parameterType.Kind == ParameterTypeKind.Array && parameterType.ArrayTypeRef is { } arrayTypeRef)
        {
            writer.WriteAttributeString("arrayTypeRef", arrayTypeRef);
        }

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
        if (parameterType.Kind == ParameterTypeKind.Array)
        {
            slots.Add(("DimensionList", () =>
            {
                writer.WriteStartElement("DimensionList", XtceNamespace);
                foreach (var dimension in parameterType.Dimensions ?? [])
                {
                    writer.WriteStartElement("Dimension", XtceNamespace);
                    WriteDimensionIndex(writer, "StartingIndex", dimension.StartingIndex);
                    WriteDimensionIndex(writer, "EndingIndex", dimension.EndingIndex);
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }));
        }
        if (parameterType.Kind == ParameterTypeKind.Aggregate)
        {
            slots.Add(("MemberList", () =>
            {
                writer.WriteStartElement("MemberList", XtceNamespace);
                foreach (var member in parameterType.Members ?? [])
                {
                    writer.WriteStartElement("Member", XtceNamespace);
                    writer.WriteAttributeString("name", member.Name);
                    writer.WriteAttributeString("typeRef", member.TypeRef);
                    if (member.InitialValue is not null)
                    {
                        writer.WriteAttributeString("initialValue", member.InitialValue);
                    }
                    WritePreservedAttributes(writer, member.PreservedAttributes);
                    WriteFragments(writer, member.Preserved);
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }));
        }
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

    private static void WriteDimensionIndex(XmlWriter writer, string elementName, DimensionIndex index)
    {
        writer.WriteStartElement(elementName, XtceNamespace);
        if (index.FixedValue is { } fixedValue)
        {
            writer.WriteStartElement("FixedValue", XtceNamespace);
            writer.WriteValue(fixedValue);
            writer.WriteEndElement();
        }
        else if (index.Raw is { } raw)
        {
            WriteFragmentXml(writer, raw.OuterXml);
        }
        writer.WriteEndElement();
    }

    /// <summary>
    /// Slot-name prefix marking "emit just BEFORE the named element's slot" — used by
    /// anchored comment fragments. U+0001 can't occur in an XML element name,
    /// so it can't collide with a real slot.
    /// </summary>
    private const char BeforeSlotPrefix = '\u0001';

    /// <summary>Slot name that sorts after every table entry — trailing comments.</summary>
    private const string EndSlotName = "\uFFFF#end";

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
            if (fragment.ElementName == CommentAnchor.ElementName)
            {
                if (fragment.Anchor is CommentAnchor.Leading or CommentAnchor.Trailing)
                {
                    continue; // emitted around the owning element's tags, not among children
                }
                var slotName = fragment.Anchor is null ? EndSlotName : BeforeSlotPrefix + fragment.Anchor;
                slots.Add((slotName, () => WriteCommentText(writer, captured.OuterXml)));
            }
            else
            {
                slots.Add((fragment.ElementName, () => WriteFragmentXml(writer, captured.OuterXml)));
            }
        }
    }


    /// <summary>
    /// Emits one preserved element fragment. Fragments that parse alone and carry a real
    /// namespace go through WriteNode, so declarations already in scope (the document's
    /// default XTCE namespace above all) are not repeated on every fragment root.
    /// Fragments with no namespace declaration of their own, or that don't parse alone
    /// (possible for documents posted as JSON), are written verbatim as before and
    /// textually inherit the document's namespaces.
    /// </summary>
    private static void WriteFragmentXml(XmlWriter writer, string outerXml)
    {
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(outerXml), settings);
            reader.MoveToContent();
            if (reader.NodeType == XmlNodeType.Element && reader.NamespaceURI.Length > 0)
            {
                // The copy runs through a NON-indenting sub-writer so the fragment's own
                // layout stays byte-identical; a wrapper element primes the sub-writer's
                // scope with the document default namespace so a matching declaration on
                // the fragment root is recognized as redundant and dropped.
                var buffer = new StringBuilder();
                using (var sub = XmlWriter.Create(buffer, new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true }))
                {
                    sub.WriteStartElement("xtce-fragment-scope", XtceNamespace);
                    CopyFragment(sub, reader);
                    sub.WriteEndElement();
                }
                var wrapped = buffer.ToString();
                var inner = wrapped[(wrapped.IndexOf('>') + 1)..wrapped.LastIndexOf("</xtce-fragment-scope>", StringComparison.Ordinal)];
                writer.WriteRaw(inner);
                return;
            }
        }
        catch (XmlException)
        {
            // Not parseable on its own — emit exactly what was carried.
        }
        writer.WriteRaw(outerXml);
    }

    /// <summary>
    /// Deep-copies a fragment with namespace-aware writes and NO copied xmlns attributes:
    /// the writer re-derives declarations, so ones already in scope (the document default)
    /// are not repeated, while foreign or prefixed ones appear exactly where first needed.
    /// (Known edge: a declaration used only by a QName inside text content would be
    /// dropped; XTCE carries no such constructs in preserved content.)
    /// </summary>
    private static void CopyFragment(XmlWriter writer, XmlReader reader)
    {
        while (!reader.EOF)
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    var isEmpty = reader.IsEmptyElement;
                    writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
                    if (reader.MoveToFirstAttribute())
                    {
                        do
                        {
                            if (reader.NamespaceURI != "http://www.w3.org/2000/xmlns/")
                            {
                                writer.WriteAttributeString(reader.Prefix, reader.LocalName,
                                    reader.NamespaceURI.Length == 0 ? null : reader.NamespaceURI, reader.Value);
                            }
                        } while (reader.MoveToNextAttribute());
                        reader.MoveToElement();
                    }
                    if (isEmpty)
                    {
                        writer.WriteEndElement();
                    }
                    break;
                case XmlNodeType.Text:
                    writer.WriteString(reader.Value);
                    break;
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    writer.WriteWhitespace(reader.Value);
                    break;
                case XmlNodeType.CDATA:
                    writer.WriteCData(reader.Value);
                    break;
                case XmlNodeType.Comment:
                    writer.WriteComment(reader.Value);
                    break;
                case XmlNodeType.ProcessingInstruction:
                    writer.WriteProcessingInstruction(reader.Name, reader.Value);
                    break;
                case XmlNodeType.EndElement:
                    writer.WriteFullEndElement();
                    break;
            }
            reader.Read();
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
            if (fragment.ElementName == CommentAnchor.ElementName)
            {
                WriteCommentText(writer, fragment.OuterXml);
            }
            else
            {
                WriteFragmentXml(writer, fragment.OuterXml);
            }
        }
    }

    /// <summary>
    /// Writes a comment, defensively adjusting text that XML forbids inside comments
    /// ("--", or a trailing "-"). Documents loaded from XML can't contain those (they'd
    /// have been unparseable), but documents posted as JSON through the API can.
    /// </summary>
    private static void WriteCommentText(XmlWriter writer, string text)
    {
        var safe = text.Replace("--", "- -");
        if (safe.EndsWith('-'))
        {
            safe += " ";
        }
        writer.WriteComment(safe);
    }

    /// <summary>Comment fragments captured before the owning element's start tag.</summary>
    private static void WriteLeadingComments(XmlWriter writer, IReadOnlyList<RawXmlFragment>? preserved)
    {
        foreach (var fragment in preserved ?? [])
        {
            if (fragment.ElementName == CommentAnchor.ElementName && fragment.Anchor == CommentAnchor.Leading)
            {
                WriteCommentText(writer, fragment.OuterXml);
            }
        }
    }

    /// <summary>Comment fragments captured after the owning element's end tag.</summary>
    private static void WriteTrailingComments(XmlWriter writer, IReadOnlyList<RawXmlFragment>? preserved)
    {
        foreach (var fragment in preserved ?? [])
        {
            if (fragment.ElementName == CommentAnchor.ElementName && fragment.Anchor == CommentAnchor.Trailing)
            {
                WriteCommentText(writer, fragment.OuterXml);
            }
        }
    }

    /// <summary>
    /// Emits slot actions stably sorted by their element name's position in the parent's
    /// XSD sequence table — preserved fragments interleave correctly with modeled elements,
    /// and same-named entries keep their captured relative order (OrderBy is stable). A name
    /// missing from the table (shouldn't happen for schema-valid input) sorts last rather
    /// than throwing: emitting it somewhere beats losing it. Comment slots prefixed with
    /// BeforeSlotPrefix sort a half-step ahead of their anchor element's slot, landing the
    /// comment immediately before the element it originally preceded.
    /// </summary>
    private static void EmitInSchemaOrder(string[] orderTable, List<(string Name, Action Emit)> slots)
    {
        foreach (var slot in slots.OrderBy(s =>
                 {
                     var before = s.Name.Length > 0 && s.Name[0] == BeforeSlotPrefix;
                     var lookup = before ? s.Name[1..] : s.Name;
                     var index = Array.IndexOf(orderTable, lookup);
                     double key = index < 0 ? orderTable.Length : index;
                     return before ? key - 0.5 : key;
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
