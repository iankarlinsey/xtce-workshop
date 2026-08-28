using System.Text;
using System.Xml;

namespace Xtce.Workshop.Model;

/// <summary>
/// Reads a SpaceSystem element (and, recursively, its nested SpaceSystem children) from
/// an XTCE document. Deliberately built on XmlReader (forward-only, non-buffering)
/// rather than XDocument/XmlDocument — XTCE files can be tens of megabytes, and the
/// reading strategy needs to survive being extended to a real streaming parser later
/// without a rewrite.
///
/// Anything the object model doesn't represent is PRESERVED, not dropped:
/// unmodeled child elements are captured verbatim via ReadOuterXml into RawXmlFragment
/// lists, and unmodeled attributes into RawAttribute lists, so XtceDocumentWriter can
/// write them back and a load → save round trip never loses data the editor didn't touch.
/// </summary>
public static class XtceDocumentReader
{
    private const string ExpectedRootElementName = "SpaceSystem";

    /// <summary>
    /// Recursive-descent parsing means SpaceSystem nesting consumes call stack — an
    /// adversarially deep document could otherwise crash the process (a DoS against the
    /// API). Real XTCE hierarchies are a handful of levels; 200 is generous.
    /// </summary>
    private const int MaxSpaceSystemDepth = 200;

    private static readonly IReadOnlyDictionary<string, ParameterTypeKind> ArgumentTypeElementKinds =
        new Dictionary<string, ParameterTypeKind>
        {
            ["IntegerArgumentType"] = ParameterTypeKind.Integer,
            ["FloatArgumentType"] = ParameterTypeKind.Float,
            ["StringArgumentType"] = ParameterTypeKind.String,
            ["BooleanArgumentType"] = ParameterTypeKind.Boolean,
            ["EnumeratedArgumentType"] = ParameterTypeKind.Enumerated,
            ["BinaryArgumentType"] = ParameterTypeKind.Binary,
            // The XSD's element name carries a typo ("Agument"); accept the correct
            // spelling too, leniently.
            ["RelativeTimeAgumentType"] = ParameterTypeKind.RelativeTime,
            ["RelativeTimeArgumentType"] = ParameterTypeKind.RelativeTime,
            ["AbsoluteTimeArgumentType"] = ParameterTypeKind.AbsoluteTime,
            ["ArrayArgumentType"] = ParameterTypeKind.Array,
            ["AggregateArgumentType"] = ParameterTypeKind.Aggregate,
        };

    private static readonly IReadOnlyDictionary<string, ParameterTypeKind> ParameterTypeElementKinds =
        new Dictionary<string, ParameterTypeKind>
        {
            ["IntegerParameterType"] = ParameterTypeKind.Integer,
            ["FloatParameterType"] = ParameterTypeKind.Float,
            ["StringParameterType"] = ParameterTypeKind.String,
            ["BooleanParameterType"] = ParameterTypeKind.Boolean,
            ["EnumeratedParameterType"] = ParameterTypeKind.Enumerated,
            ["BinaryParameterType"] = ParameterTypeKind.Binary,
            ["RelativeTimeParameterType"] = ParameterTypeKind.RelativeTime,
            ["AbsoluteTimeParameterType"] = ParameterTypeKind.AbsoluteTime,
            ["ArrayParameterType"] = ParameterTypeKind.Array,
            ["AggregateParameterType"] = ParameterTypeKind.Aggregate,
        };

    private static readonly IReadOnlyDictionary<string, DataEncodingKind> DataEncodingElementKinds =
        new Dictionary<string, DataEncodingKind>
        {
            ["IntegerDataEncoding"] = DataEncodingKind.Integer,
            ["FloatDataEncoding"] = DataEncodingKind.Float,
            ["StringDataEncoding"] = DataEncodingKind.String,
            ["BinaryDataEncoding"] = DataEncodingKind.Binary,
        };

    public static SpaceSystem Load(Stream xmlStream)
    {
        var result = LoadCore(xmlStream, recovery: null);
        return result.Document!; // non-recovery mode throws instead of returning diagnostics
    }

    /// <summary>
    /// Best-effort load: parses everything it can, quarantining each unparseable modeled
    /// element as a preserved fragment (verbatim round-trip) with one positioned
    /// diagnostic per problem. Document is null only when nothing was loadable
    /// (malformed XML, unusable root element).
    /// </summary>
    public static XtceLoadResult LoadWithRecovery(Stream xmlStream)
    {
        var recovery = new RecoveryContext();
        try
        {
            return LoadCore(xmlStream, recovery);
        }
        catch (XtceParseException ex)
        {
            recovery.Add(new LoadDiagnostic(LoadDiagnosticKind.ModelError, ex.Message, "(document root)", null, null));
            return new XtceLoadResult(null, recovery.Diagnostics, recovery.Positions);
        }
    }

    private static XtceLoadResult LoadCore(Stream xmlStream, RecoveryContext? recovery)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        using var reader = XmlReader.Create(xmlStream, settings);

        try
        {
            // Walk to the root element by hand instead of MoveToContent, which would
            // silently skip document-prolog comments (license headers are common in real
            // XTCE files).
            List<string>? prologComments = null;
            while (reader.NodeType != XmlNodeType.Element)
            {
                if (reader.NodeType == XmlNodeType.Comment)
                {
                    (prologComments ??= new List<string>()).Add(reader.Value);
                }
                if (!reader.Read())
                {
                    throw new XtceParseException("The document has no root element.");
                }
            }

            if (recovery is not null && reader is IXmlLineInfo rootLineInfo && rootLineInfo.HasLineInfo())
            {
                var rootName = reader.GetAttribute("name");
                if (!string.IsNullOrEmpty(rootName))
                {
                    recovery.RecordPosition(rootName, rootLineInfo.LineNumber, rootLineInfo.LinePosition);
                }
            }

            var root = ReadSpaceSystem(reader, depth: 0, TakeLeadingComments(ref prologComments), recovery);

            // Document-epilog comments after the root's end tag.
            List<string>? epilogComments = null;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Comment)
                {
                    (epilogComments ??= new List<string>()).Add(reader.Value);
                }
            }
            if (epilogComments is not null)
            {
                var trailing = epilogComments
                    .Select(text => new RawXmlFragment(CommentAnchor.ElementName, text, CommentAnchor.Trailing));
                root = root with { Preserved = [.. root.Preserved ?? [], .. trailing] };
            }

            return new XtceLoadResult(root, recovery?.Diagnostics ?? [], recovery?.Positions);
        }
        catch (XmlException ex)
        {
            if (recovery is null)
            {
                throw new XtceParseException("The document is not well-formed XML.", ex);
            }
            recovery.Add(new LoadDiagnostic(
                LoadDiagnosticKind.MalformedXml,
                $"Not well-formed XML: {ex.Message}",
                "(document)",
                ex.LineNumber > 0 ? ex.LineNumber : null,
                ex.LinePosition > 0 ? ex.LinePosition : null));
            return new XtceLoadResult(null, recovery.Diagnostics, recovery.Positions);
        }
    }

    // ---- best-effort recovery (quarantine + diagnostics) -------------------------------

    private sealed class RecoveryContext
    {
        private const int MaxDiagnostics = 200;
        public List<LoadDiagnostic> Diagnostics { get; } = new();

        /// <summary>
        /// Sub-readers report positions relative to their fragment; this offset maps them
        /// back to document lines (fragment line 1 == the wrapped element's line).
        /// </summary>
        public int LineOffset { get; set; }

        /// <summary>Element positions keyed by the validator's location grammar.</summary>
        public Dictionary<string, LoadPosition> Positions { get; } = new();

        public void Add(LoadDiagnostic diagnostic)
        {
            if (Diagnostics.Count < MaxDiagnostics)
            {
                Diagnostics.Add(diagnostic);
            }
        }

        /// <summary>First occurrence wins — duplicate names are their own validation finding.</summary>
        public void RecordPosition(string locationPath, int? line, int? column)
        {
            if (line is not null && !Positions.ContainsKey(locationPath))
            {
                Positions[locationPath] = new LoadPosition(line.Value, column ?? 1);
            }
        }
    }

    /// <summary>
    /// Reads one modeled element in recovery mode: the subtree is captured FIRST (with
    /// its position), then parsed from a sub-reader — so a failure mid-element cannot
    /// strand the outer reader. On failure the verbatim subtree goes to the quarantine
    /// list and one diagnostic is recorded; the sibling loop continues either way.
    /// </summary>
    private static void ReadItemWithRecovery(
        XmlReader reader,
        RecoveryContext recovery,
        string parentPath,
        Action<XmlReader> parse,
        ref List<RawXmlFragment>? quarantine)
    {
        var lineInfo = reader as IXmlLineInfo;
        int? line = lineInfo?.HasLineInfo() == true ? lineInfo.LineNumber + recovery.LineOffset : null;
        int? column = lineInfo?.HasLineInfo() == true ? lineInfo.LinePosition : null;
        var elementName = reader.LocalName;
        var nameAttribute = reader.GetAttribute("name");
        if (nameAttribute is not null)
        {
            recovery.RecordPosition($"{parentPath}/{nameAttribute}", line, column);
        }
        var outerXml = reader.ReadOuterXml();

        var savedOffset = recovery.LineOffset;
        try
        {
            recovery.LineOffset = (line ?? 1) - 1; // nested sub-reader line 1 == this element's document line
            using var subReader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            subReader.MoveToContent();
            parse(subReader);
        }
        catch (XtceParseException ex)
        {
            var path = nameAttribute is null
                ? $"{parentPath}/{elementName}"
                : $"{parentPath}/{elementName}[{nameAttribute}]";
            recovery.Add(new LoadDiagnostic(LoadDiagnosticKind.ModelError, ex.Message, path, line, column));
            (quarantine ??= new List<RawXmlFragment>()).Add(new RawXmlFragment(elementName, outerXml));
        }
        finally
        {
            recovery.LineOffset = savedOffset;
        }
    }

    /// <summary>
    /// Reads one SpaceSystem element, positioned at its start tag, consuming through its
    /// matching end tag (or itself, if empty) — so the caller's reader ends up positioned
    /// exactly where a sibling-or-parent's own loop expects it next.
    /// </summary>
    private static SpaceSystem ReadSpaceSystem(
        XmlReader reader, int depth, List<RawXmlFragment>? leadingComments = null, RecoveryContext? recovery = null, string parentPath = "")
    {
        if (depth >= MaxSpaceSystemDepth)
        {
            throw new XtceParseException(
                $"SpaceSystem nesting exceeds the supported depth of {MaxSpaceSystemDepth}.");
        }
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
        var path = parentPath.Length == 0 ? name : $"{parentPath}/{name}";

        var children = new List<SpaceSystem>();
        TelemetryMetaData? telemetryMetaData = null;
        CommandMetaData? commandMetaData = null;
        Header? header = null;
        List<Service>? services = null;
        var preserved = leadingComments;
        List<string>? pendingComments = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return new SpaceSystem(name, children, telemetryMetaData, preserved, preservedAttributes, commandMetaData, header, services);
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == ExpectedRootElementName)
            {
                var leading = TakeLeadingComments(ref pendingComments);
                if (recovery is null)
                {
                    children.Add(ReadSpaceSystem(reader, depth + 1, leading));
                }
                else
                {
                    ReadItemWithRecovery(reader, recovery, path,
                        r => children.Add(ReadSpaceSystem(r, depth + 1, leading, recovery, path)), ref preserved);
                }
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Header" && header is null)
            {
                DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                header = ReadHeader(reader);
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ServiceSet" && services is null)
            {
                DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                services = new List<Service>();
                ReadServiceSet(reader, services);
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "TelemetryMetaData")
            {
                DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                if (recovery is null)
                {
                    telemetryMetaData = ReadTelemetryMetaData(reader);
                }
                else
                {
                    ReadItemWithRecovery(reader, recovery, path,
                        r => telemetryMetaData = ReadTelemetryMetaData(r, recovery, path), ref preserved);
                }
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "CommandMetaData")
            {
                DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                if (recovery is null)
                {
                    commandMetaData = ReadCommandMetaData(reader);
                }
                else
                {
                    ReadItemWithRecovery(reader, recovery, path,
                        r => commandMetaData = ReadCommandMetaData(r, recovery, path), ref preserved);
                }
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // Unmodeled child (LongDescription, AliasSet, AncillaryDataSet,
                // ServiceSet) — preserved verbatim, re-emitted on save.
                DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                Preserve(ref preserved, reader);
            }
            else if (!TryCaptureComment(reader, ref pendingComments))
            {
                reader.Read();
            }
        }

        DrainComments(ref preserved, ref pendingComments, null);
        reader.ReadEndElement();

        return new SpaceSystem(name, children, telemetryMetaData, preserved, preservedAttributes, commandMetaData, header, services);
    }

    private static CommandMetaData ReadCommandMetaData(XmlReader reader, RecoveryContext? recovery = null, string path = "")
    {
        var metaCommands = new List<MetaCommand>();
        List<ParameterTypeDefinition>? argumentTypes = null;
        List<RawXmlFragment>? preservedArgumentTypes = null;
        List<ParameterTypeDefinition>? parameterTypes = null;
        List<RawXmlFragment>? preservedParameterTypes = null;
        List<Parameter>? parameters = null;
        List<RawXmlFragment>? preservedParameters = null;
        List<Algorithm>? algorithms = null;
        List<RawXmlFragment>? preservedAlgorithms = null;
        List<CommandContainer>? commandContainers = null;
        List<RawXmlFragment>? preservedCommandContainers = null;
        List<StreamDefinition>? commandStreams = null;
        List<BlockMetaCommand>? blockMetaCommands = null;
        List<string>? metaCommandRefs = null;
        List<RawXmlFragment>? preservedEntries = null;
        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return new CommandMetaData(metaCommands);
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "MetaCommandSet")
            {
                DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                ReadMetaCommandSet(reader, metaCommands, ref preservedEntries,
                    ref blockMetaCommands, ref metaCommandRefs, recovery, path);
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ArgumentTypeSet")
            {
                DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                argumentTypes ??= new List<ParameterTypeDefinition>();
                ReadArgumentTypeSet(reader, argumentTypes, ref preservedArgumentTypes, recovery, path);
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ParameterTypeSet")
            {
                // The command side's own parameter types — same element content as the
                // telemetry set, so the same reader runs with a CommandMetaData-scoped
                // recovery path.
                DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                parameterTypes ??= new List<ParameterTypeDefinition>();
                ReadParameterTypeSet(reader, parameterTypes, ref preservedParameterTypes, recovery, $"{path}/CommandMetaData");
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ParameterSet")
            {
                DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                parameters ??= new List<Parameter>();
                ReadParameterSet(reader, parameters, ref preservedParameters, recovery, $"{path}/CommandMetaData");
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "AlgorithmSet" && algorithms is null)
            {
                DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                algorithms = new List<Algorithm>();
                ReadAlgorithmSet(reader, algorithms, ref preservedAlgorithms);
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "CommandContainerSet"
                     && commandContainers is null)
            {
                DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                commandContainers = new List<CommandContainer>();
                ReadCommandContainerSet(reader, commandContainers, ref preservedCommandContainers);
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "StreamSet"
                     && commandStreams is null)
            {
                DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                commandStreams = new List<StreamDefinition>();
                ReadStreamSet(reader, commandStreams);
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                Preserve(ref preserved, reader);
            }
            else if (!TryCaptureComment(reader, ref pendingComments))
            {
                reader.Read();
            }
        }

        DrainComments(ref preserved, ref pendingComments, null);
        reader.ReadEndElement();

        return new CommandMetaData(metaCommands, preservedEntries, preserved, argumentTypes, preservedArgumentTypes,
            parameterTypes, preservedParameterTypes, parameters, preservedParameters, algorithms, preservedAlgorithms,
            commandContainers, preservedCommandContainers, commandStreams, blockMetaCommands, metaCommandRefs);
    }

    private static void ReadArgumentTypeSet(
        XmlReader reader,
        List<ParameterTypeDefinition> argumentTypes,
        ref List<RawXmlFragment>? preservedTypes,
        RecoveryContext? recovery = null,
        string path = "")
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();

        List<string>? pendingComments = null;
        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element &&
                ArgumentTypeElementKinds.TryGetValue(reader.LocalName, out var kind))
            {
                var leading = TakeLeadingComments(ref pendingComments);
                if (recovery is null)
                {
                    argumentTypes.Add(ReadParameterTypeDefinition(reader, kind, leading));
                }
                else
                {
                    ReadItemWithRecovery(reader, recovery, $"{path}/CommandMetaData/ArgumentTypeSet",
                        r => argumentTypes.Add(ReadParameterTypeDefinition(r, kind, leading)), ref preservedTypes);
                }
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                DrainComments(ref preservedTypes, ref pendingComments, reader.LocalName);
                Preserve(ref preservedTypes, reader);
            }
            else if (!TryCaptureComment(reader, ref pendingComments))
            {
                reader.Read();
            }
        }

        DrainComments(ref preservedTypes, ref pendingComments, null);
        reader.ReadEndElement();
    }

    private static void ReadMetaCommandSet(
        XmlReader reader, List<MetaCommand> metaCommands, ref List<RawXmlFragment>? preservedEntries,
        ref List<BlockMetaCommand>? blockMetaCommands, ref List<string>? metaCommandRefs,
        RecoveryContext? recovery = null, string path = "")
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();

        List<string>? pendingComments = null;
        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "MetaCommand")
            {
                var leading = TakeLeadingComments(ref pendingComments);
                if (recovery is null)
                {
                    metaCommands.Add(ReadMetaCommand(reader, leading));
                }
                else
                {
                    ReadItemWithRecovery(reader, recovery, $"{path}/CommandMetaData/MetaCommandSet",
                        r => metaCommands.Add(ReadMetaCommand(r, leading, recovery, $"{path}/CommandMetaData/MetaCommandSet")), ref preservedEntries);
                }
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "BlockMetaCommand"
                     && reader.GetAttribute("name") is not null)
            {
                DrainComments(ref preservedEntries, ref pendingComments, reader.LocalName);
                (blockMetaCommands ??= new List<BlockMetaCommand>()).Add(ReadBlockMetaCommand(reader));
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "MetaCommandRef"
                     && !reader.IsEmptyElement && reader.AttributeCount == 0)
            {
                DrainComments(ref preservedEntries, ref pendingComments, reader.LocalName);
                var outerXml = reader.ReadOuterXml();
                if (TryReadTextOnlyElement(outerXml, out var reference))
                {
                    (metaCommandRefs ??= new List<string>()).Add(reference.Trim());
                }
                else
                {
                    (preservedEntries ??= new List<RawXmlFragment>()).Add(new RawXmlFragment("MetaCommandRef", outerXml));
                }
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // Foreign or unmodelable set entries — preserved in the set.
                DrainComments(ref preservedEntries, ref pendingComments, reader.LocalName);
                Preserve(ref preservedEntries, reader);
            }
            else if (!TryCaptureComment(reader, ref pendingComments))
            {
                reader.Read();
            }
        }

        DrainComments(ref preservedEntries, ref pendingComments, null);
        reader.ReadEndElement();
    }

    private static MetaCommand ReadMetaCommand(
        XmlReader reader, List<RawXmlFragment>? leadingComments = null,
        RecoveryContext? recovery = null, string? setPath = null)
    {
        var name = RequireAttribute(reader, "name", "a MetaCommand");
        var commandPath = setPath is null ? null : $"{setPath}/{name}";
        var isAbstract = ParseBool(reader, "abstract");
        var preservedAttributes = CapturePreservedAttributes(reader, "name", "abstract");

        string? baseMetaCommandRef = null;
        List<RawXmlFragment>? basePreserved = null;
        List<Argument>? arguments = null;
        List<RawXmlFragment>? preservedArguments = null;
        List<ArgumentAssignment>? argumentAssignments = null;
        List<CommandVerifier>? verifiers = null;
        List<TransmissionConstraint>? transmissionConstraints = null;
        List<ParameterToSet>? parameterToSets = null;
        Significance? defaultSignificance = null;
        List<ContextSignificance>? contextSignificances = null;
        Interlock? interlock = null;
        Description? description = null;
        var preserved = leadingComments;
        List<string>? pendingComments = null;
        CommandContainer? commandContainer = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "BaseMetaCommand")
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    baseMetaCommandRef = RequireAttribute(reader, "metaCommandRef", "a BaseMetaCommand");
                    if (reader.IsEmptyElement)
                    {
                        reader.Read();
                    }
                    else
                    {
                        reader.ReadStartElement();
                        List<string>? basePendingComments = null;
                        while (reader.NodeType != XmlNodeType.EndElement)
                        {
                            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ArgumentAssignmentList")
                            {
                                DrainComments(ref basePreserved, ref basePendingComments, reader.LocalName);
                                argumentAssignments ??= new List<ArgumentAssignment>();
                                ReadArgumentAssignmentList(reader, argumentAssignments);
                            }
                            else if (reader.NodeType == XmlNodeType.Element)
                            {
                                DrainComments(ref basePreserved, ref basePendingComments, reader.LocalName);
                                Preserve(ref basePreserved, reader);
                            }
                            else if (!TryCaptureComment(reader, ref basePendingComments))
                            {
                                reader.Read();
                            }
                        }
                        DrainComments(ref basePreserved, ref basePendingComments, null);
                        reader.ReadEndElement();
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ArgumentList")
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    arguments ??= new List<Argument>();
                    ReadArgumentList(reader, arguments, ref preservedArguments);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "VerifierSet" && verifiers is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    verifiers = new List<CommandVerifier>();
                    ReadVerifierSet(reader, verifiers);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "TransmissionConstraintList"
                         && transmissionConstraints is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    transmissionConstraints = new List<TransmissionConstraint>();
                    ReadTransmissionConstraintList(reader, transmissionConstraints);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "DefaultSignificance"
                         && defaultSignificance is null && reader.IsEmptyElement)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    defaultSignificance = new Significance(
                        reader.GetAttribute("spaceSystemAtRisk"),
                        reader.GetAttribute("reasonForWarning"),
                        reader.GetAttribute("consequenceLevel"),
                        CapturePreservedAttributes(reader, ["spaceSystemAtRisk", "reasonForWarning", "consequenceLevel"]));
                    reader.Read();
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ContextSignificanceList"
                         && contextSignificances is null && HasOnlyAttributes(reader))
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    var outerXml = reader.ReadOuterXml();
                    if (TryParseContextSignificanceList(outerXml, out var parsedSignificances))
                    {
                        contextSignificances = parsedSignificances;
                    }
                    else
                    {
                        (preserved ??= new List<RawXmlFragment>()).Add(
                            new RawXmlFragment("ContextSignificanceList", outerXml));
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Interlock"
                         && interlock is null && reader.IsEmptyElement)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    interlock = new Interlock(
                        reader.GetAttribute("scopeToSpaceSystem"),
                        reader.GetAttribute("verificationToWaitFor"),
                        reader.GetAttribute("verificationProgressPercentage"),
                        ParseBool(reader, "suspendable"),
                        CapturePreservedAttributes(reader,
                            ["scopeToSpaceSystem", "verificationToWaitFor", "verificationProgressPercentage", "suspendable"]));
                    reader.Read();
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ParameterToSetList"
                         && parameterToSets is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    parameterToSets = new List<ParameterToSet>();
                    ReadParameterToSetList(reader, parameterToSets);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "CommandContainer")
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    if (recovery is not null && commandPath is not null && reader is IXmlLineInfo containerLineInfo && containerLineInfo.HasLineInfo())
                    {
                        recovery.RecordPosition($"{commandPath}/CommandContainer",
                            containerLineInfo.LineNumber + recovery.LineOffset, containerLineInfo.LinePosition);
                    }
                    commandContainer = ReadCommandContainer(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element
                         && TryReadDescriptionChild(reader, ref description, ref preserved, ref pendingComments))
                {
                    // description-trio child handled
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new MetaCommand(
            name, isAbstract, baseMetaCommandRef, basePreserved,
            verifiers, preserved, preservedAttributes,
            commandContainer, arguments, preservedArguments, argumentAssignments,
            transmissionConstraints, parameterToSets, defaultSignificance, interlock, description,
            contextSignificances);
    }

    private static BlockMetaCommand ReadBlockMetaCommand(XmlReader reader)
    {
        var name = RequireAttribute(reader, "name", "a BlockMetaCommand");
        var preservedAttributes = CapturePreservedAttributes(reader, "name");

        List<MetaCommandStep>? steps = null;
        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;
        Description? description = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element
                    && TryReadDescriptionChild(reader, ref description, ref preserved, ref pendingComments))
                {
                    // description-trio child handled
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "MetaCommandStepList"
                         && steps is null && HasOnlyAttributes(reader))
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    var outerXml = reader.ReadOuterXml();
                    if (TryParseMetaCommandStepList(outerXml, out var parsedSteps))
                    {
                        steps = parsedSteps;
                    }
                    else
                    {
                        (preserved ??= new List<RawXmlFragment>()).Add(
                            new RawXmlFragment("MetaCommandStepList", outerXml));
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new BlockMetaCommand(name, steps, preserved, preservedAttributes, description);
    }

    /// <summary>
    /// Strict parse of a MetaCommandStepList; false means the caller preserves the whole
    /// list verbatim (partial-parse rollback — a step missing its required metaCommandRef,
    /// embedded comments, or unmodelable assignment shapes all bail rather than drop).
    /// </summary>
    private static bool TryParseMetaCommandStepList(string outerXml, out List<MetaCommandStep> steps)
    {
        steps = new List<MetaCommandStep>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            reader.Read();
            if (reader.IsEmptyElement)
            {
                return true;
            }
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "MetaCommandStep"
                    && reader.GetAttribute("metaCommandRef") is { } metaCommandRef)
                {
                    var preservedAttributes = CapturePreservedAttributes(reader, "metaCommandRef");
                    List<ArgumentAssignment>? assignments = null;
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
                            // "ArgumentAssigmentList" is the XSD's own typo; accept the
                            // corrected spelling too. The writer re-emits the typo.
                            if (reader.NodeType == XmlNodeType.Element
                                && reader.LocalName is "ArgumentAssigmentList" or "ArgumentAssignmentList"
                                && assignments is null && HasOnlyAttributes(reader))
                            {
                                assignments = new List<ArgumentAssignment>();
                                if (!TryReadStrictArgumentAssignments(reader, assignments))
                                {
                                    return false;
                                }
                            }
                            else if (reader.NodeType == XmlNodeType.Element)
                            {
                                Preserve(ref preserved, reader);
                            }
                            else if (reader.NodeType is XmlNodeType.Comment or XmlNodeType.ProcessingInstruction)
                            {
                                return false;
                            }
                            else
                            {
                                reader.Read();
                            }
                        }
                        reader.ReadEndElement();
                    }

                    steps.Add(new MetaCommandStep(metaCommandRef, assignments, preserved, preservedAttributes));
                }
                else if (reader.NodeType is XmlNodeType.Element or XmlNodeType.Comment or XmlNodeType.ProcessingInstruction)
                {
                    return false;
                }
                else
                {
                    reader.Read();
                }
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads an assignment list allowing only empty ArgumentAssignment elements with
    /// exactly the two required attributes; anything else fails the step-list parse.
    /// </summary>
    private static bool TryReadStrictArgumentAssignments(XmlReader reader, List<ArgumentAssignment> assignments)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return true;
        }
        reader.ReadStartElement();
        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ArgumentAssignment"
                && reader.IsEmptyElement && reader.AttributeCount == 2
                && reader.GetAttribute("argumentName") is { } argumentName
                && reader.GetAttribute("argumentValue") is { } argumentValue)
            {
                assignments.Add(new ArgumentAssignment(argumentName, argumentValue));
                reader.Read();
            }
            else if (reader.NodeType is XmlNodeType.Element or XmlNodeType.Comment or XmlNodeType.ProcessingInstruction)
            {
                return false;
            }
            else
            {
                reader.Read();
            }
        }
        reader.ReadEndElement();
        return true;
    }

    /// <summary>
    /// Strict parse of a ContextSignificanceList; false preserves the whole list.
    /// An entry models only as required ContextMatch + attributes-only Significance
    /// (matching the DefaultSignificance shape) — anything else rides raw in position
    /// (first matching context wins, so order is meaning).
    /// </summary>
    private static bool TryParseContextSignificanceList(string outerXml, out List<ContextSignificance> entries)
    {
        entries = new List<ContextSignificance>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            reader.MoveToContent();
            if (reader.IsEmptyElement)
            {
                return true;
            }
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ContextSignificance")
                {
                    var entryXml = reader.ReadOuterXml();
                    entries.Add(TryParseContextSignificance(entryXml, out var entry)
                        ? entry
                        : new ContextSignificance(RawXml: new RawXmlFragment("ContextSignificance", entryXml)));
                }
                else if (reader.NodeType is XmlNodeType.Element or XmlNodeType.Comment or XmlNodeType.ProcessingInstruction)
                {
                    return false;
                }
                else
                {
                    reader.Read();
                }
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool TryParseContextSignificance(string outerXml, out ContextSignificance entry)
    {
        entry = new ContextSignificance();
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            reader.MoveToContent();
            if (!HasOnlyAttributes(reader) || reader.IsEmptyElement)
            {
                return false;
            }
            reader.ReadStartElement();

            MatchCriteria? context = null;
            Significance? significance = null;

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ContextMatch"
                    && context is null)
                {
                    context = ReadMatchCriteria(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Significance"
                         && significance is null && reader.IsEmptyElement)
                {
                    significance = new Significance(
                        reader.GetAttribute("spaceSystemAtRisk"),
                        reader.GetAttribute("reasonForWarning"),
                        reader.GetAttribute("consequenceLevel"),
                        CapturePreservedAttributes(reader, ["spaceSystemAtRisk", "reasonForWarning", "consequenceLevel"]));
                    reader.Read();
                }
                else if (reader.NodeType is XmlNodeType.Element or XmlNodeType.Comment or XmlNodeType.ProcessingInstruction)
                {
                    return false;
                }
                else
                {
                    reader.Read();
                }
            }

            if (context is null || significance is null)
            {
                return false; // the XSD requires both halves
            }
            entry = new ContextSignificance(context, significance);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static void ReadTransmissionConstraintList(XmlReader reader, List<TransmissionConstraint> constraints)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "TransmissionConstraint")
            {
                constraints.Add(ReadTransmissionConstraint(reader));
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                var elementName = reader.LocalName;
                constraints.Add(new TransmissionConstraint(
                    RawXml: new RawXmlFragment(elementName, reader.ReadOuterXml())));
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();
    }

    private static TransmissionConstraint ReadTransmissionConstraint(XmlReader reader)
    {
        var timeOut = reader.GetAttribute("timeOut");
        var suspendable = ParseBool(reader, "suspendable");
        var preservedAttributes = CapturePreservedAttributes(reader, ["timeOut", "suspendable"]);

        Comparison? comparison = null;
        List<Comparison>? comparisonList = null;
        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Comparison"
                    && comparison is null && reader.GetAttribute("parameterRef") is not null
                    && reader.GetAttribute("value") is not null && reader.IsEmptyElement)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    comparison = ReadComparison(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ComparisonList"
                         && comparisonList is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    comparisonList = ReadComparisonList(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // BooleanExpression, CustomAlgorithm — preserved verbatim.
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new TransmissionConstraint(timeOut, suspendable, comparison, comparisonList, preserved, preservedAttributes);
    }

    private static void ReadParameterToSetList(XmlReader reader, List<ParameterToSet> parameterToSets)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ParameterToSet"
                && reader.GetAttribute("parameterRef") is not null)
            {
                parameterToSets.Add(ReadParameterToSet(reader));
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                var elementName = reader.LocalName;
                parameterToSets.Add(new ParameterToSet(
                    RawXml: new RawXmlFragment(elementName, reader.ReadOuterXml())));
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();
    }

    private static ParameterToSet ReadParameterToSet(XmlReader reader)
    {
        var parameterRef = reader.GetAttribute("parameterRef");
        var setOnVerification = reader.GetAttribute("setOnVerification");
        var preservedAttributes = CapturePreservedAttributes(reader, ["parameterRef", "setOnVerification"]);

        string? newValue = null;
        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "NewValue" && newValue is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    var outerXml = reader.ReadOuterXml();
                    if (TryReadTextOnlyElement(outerXml, out var text))
                    {
                        newValue = text;
                    }
                    else
                    {
                        (preserved ??= new List<RawXmlFragment>()).Add(new RawXmlFragment("NewValue", outerXml));
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // Derivation — preserved verbatim.
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new ParameterToSet(parameterRef, newValue, setOnVerification, preserved, preservedAttributes);
    }

    private static void ReadArgumentList(XmlReader reader, List<Argument> arguments, ref List<RawXmlFragment>? preservedArguments)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();
        List<string>? pendingComments = null;
        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Argument")
            {
                var name = RequireAttribute(reader, "name", "an Argument");
                var typeRef = RequireAttribute(reader, "argumentTypeRef", "an Argument");
                var initialValue = reader.GetAttribute("initialValue");
                var preservedAttributes = CapturePreservedAttributes(reader, "name", "argumentTypeRef", "initialValue");

                List<RawXmlFragment>? preserved = null;
                List<string>? argumentPendingComments = null;
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
                            DrainComments(ref preserved, ref argumentPendingComments, reader.LocalName);
                            Preserve(ref preserved, reader);
                        }
                        else if (!TryCaptureComment(reader, ref argumentPendingComments))
                        {
                            reader.Read();
                        }
                    }
                    DrainComments(ref preserved, ref argumentPendingComments, null);
                    reader.ReadEndElement();
                }
                arguments.Add(new Argument(name, typeRef, initialValue, preserved, preservedAttributes));
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                DrainComments(ref preservedArguments, ref pendingComments, reader.LocalName);
                Preserve(ref preservedArguments, reader);
            }
            else if (!TryCaptureComment(reader, ref pendingComments))
            {
                reader.Read();
            }
        }
        DrainComments(ref preservedArguments, ref pendingComments, null);
        reader.ReadEndElement();
    }

    private static void ReadArgumentAssignmentList(XmlReader reader, List<ArgumentAssignment> assignments)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }
        reader.ReadStartElement();
        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ArgumentAssignment")
            {
                var argumentName = RequireAttribute(reader, "argumentName", "an ArgumentAssignment");
                var argumentValue = reader.GetAttribute("argumentValue")
                    ?? throw new XtceParseException("an ArgumentAssignment element is missing its required 'argumentValue' attribute.");
                assignments.Add(new ArgumentAssignment(argumentName, argumentValue));
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
    }

    private static void ReadCommandContainerSet(
        XmlReader reader, List<CommandContainer> containers, ref List<RawXmlFragment>? preservedContainers)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "CommandContainer"
                && reader.GetAttribute("name") is not null)
            {
                containers.Add(ReadCommandContainer(reader));
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                Preserve(ref preservedContainers, reader);
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();
    }

    private static CommandContainer ReadCommandContainer(XmlReader reader)
    {
        var name = RequireAttribute(reader, "name", "a CommandContainer");
        var preservedAttributes = CapturePreservedAttributes(reader, "name");

        string? baseContainerRef = null;
        List<RawXmlFragment>? basePreserved = null;
        List<RawXmlFragment>? preserved = null;
        List<SequenceEntry>? entryList = null;
        List<string>? pendingComments = null;
        Description? description = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "BaseContainer")
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    baseContainerRef = RequireAttribute(reader, "containerRef", "a CommandContainer BaseContainer");
                    if (reader.IsEmptyElement)
                    {
                        reader.Read();
                    }
                    else
                    {
                        reader.ReadStartElement();
                        List<string>? basePendingComments = null;
                        while (reader.NodeType != XmlNodeType.EndElement)
                        {
                            if (reader.NodeType == XmlNodeType.Element)
                            {
                                DrainComments(ref basePreserved, ref basePendingComments, reader.LocalName);
                                Preserve(ref basePreserved, reader);
                            }
                            else if (!TryCaptureComment(reader, ref basePendingComments))
                            {
                                reader.Read();
                            }
                        }
                        DrainComments(ref basePreserved, ref basePendingComments, null);
                        reader.ReadEndElement();
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "EntryList" && entryList is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    entryList = new List<SequenceEntry>();
                    ReadEntryList(reader, entryList, commandEntries: true);
                }
                else if (reader.NodeType == XmlNodeType.Element
                         && TryReadDescriptionChild(reader, ref description, ref preserved, ref pendingComments))
                {
                    // description-trio child handled
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // BinaryEncoding, DefaultRateInStream — preserved verbatim.
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new CommandContainer(name, baseContainerRef, basePreserved, preserved, preservedAttributes, entryList, description);
    }

    private static readonly string[] VerifierElementNames =
    [
        "TransferredToRangeVerifier", "SentFromRangeVerifier", "ReceivedVerifier",
        "AcceptedVerifier", "QueuedVerifier", "ExecutionVerifier", "CompleteVerifier", "FailedVerifier",
    ];

    private static void ReadVerifierSet(XmlReader reader, List<CommandVerifier> verifiers)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && VerifierElementNames.Contains(reader.LocalName))
            {
                verifiers.Add(ReadCommandVerifier(reader));
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // A foreign element in verifier position — carried as an opaque verifier.
                var elementName = reader.LocalName;
                var outerXml = reader.ReadOuterXml();
                verifiers.Add(new CommandVerifier(elementName,
                    RawXml: new RawXmlFragment(elementName, outerXml)));
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();
    }

    private static CommandVerifier ReadCommandVerifier(XmlReader reader)
    {
        var kind = reader.LocalName;
        var preservedAttributes = CapturePreservedAttributes(reader);

        Comparison? comparison = null;
        List<Comparison>? comparisonList = null;
        string? containerRef = null;
        var hasCheckWindow = false;
        string? timeToStartChecking = null;
        string? timeToStopChecking = null;
        string? timeWindowIsRelativeTo = null;
        IReadOnlyList<RawAttribute>? checkWindowPreserved = null;
        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Comparison"
                    && comparison is null && reader.GetAttribute("parameterRef") is not null
                    && reader.GetAttribute("value") is not null && reader.IsEmptyElement)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    comparison = ReadComparison(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ComparisonList"
                         && comparisonList is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    comparisonList = ReadComparisonList(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ContainerRef"
                         && containerRef is null && reader.GetAttribute("containerRef") is not null
                         && reader.IsEmptyElement)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    containerRef = reader.GetAttribute("containerRef");
                    reader.Read();
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "CheckWindow"
                         && !hasCheckWindow && reader.IsEmptyElement)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    hasCheckWindow = true;
                    timeToStartChecking = reader.GetAttribute("timeToStartChecking");
                    timeToStopChecking = reader.GetAttribute("timeToStopChecking");
                    timeWindowIsRelativeTo = reader.GetAttribute("timeWindowIsRelativeTo");
                    checkWindowPreserved = CapturePreservedAttributes(
                        reader, ["timeToStartChecking", "timeToStopChecking", "timeWindowIsRelativeTo"]);
                    reader.Read();
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // BooleanExpression, CustomAlgorithm, ParameterValueChange,
                    // CheckWindowAlgorithms, description children — preserved verbatim.
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new CommandVerifier(kind, comparison, comparisonList, containerRef, hasCheckWindow,
            timeToStartChecking, timeToStopChecking, timeWindowIsRelativeTo, checkWindowPreserved,
            preserved, preservedAttributes);
    }

    private static void ReadServiceSet(XmlReader reader, List<Service> services)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Service"
                && reader.GetAttribute("name") is { } name)
            {
                services.Add(ReadService(reader, name));
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                var elementName = reader.LocalName;
                services.Add(new Service(elementName, RawXml: new RawXmlFragment(elementName, reader.ReadOuterXml())));
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();
    }

    private static Service ReadService(XmlReader reader, string name)
    {
        var preservedAttributes = CapturePreservedAttributes(reader, "name");

        List<string>? containerRefs = null;
        List<string>? messageRefs = null;
        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;
        Description? description = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element
                    && TryReadDescriptionChild(reader, ref description, ref preserved, ref pendingComments))
                {
                    // description-trio child handled
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ContainerRefSet"
                         && containerRefs is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    containerRefs = new List<string>();
                    var refs = containerRefs;
                    ReadSetOfRows(reader, "ContainerRef", element =>
                    {
                        if (element.GetAttribute("containerRef") is not { } reference)
                        {
                            return false;
                        }
                        refs.Add(reference);
                        return true;
                    }, ref preserved);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "MessageRefSet"
                         && messageRefs is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    messageRefs = new List<string>();
                    var refs = messageRefs;
                    ReadSetOfRows(reader, "MessageRef", element =>
                    {
                        if (element.GetAttribute("messageRef") is not { } reference)
                        {
                            return false;
                        }
                        refs.Add(reference);
                        return true;
                    }, ref preserved);
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new Service(name, containerRefs, messageRefs, preserved, preservedAttributes, description);
    }

    private static Header ReadHeader(XmlReader reader)
    {
        var version = reader.GetAttribute("version");
        var date = reader.GetAttribute("date");
        var classification = reader.GetAttribute("classification");
        var classificationInstructions = reader.GetAttribute("classificationInstructions");
        var validationStatus = reader.GetAttribute("validationStatus");
        var preservedAttributes = CapturePreservedAttributes(reader,
            ["version", "date", "classification", "classificationInstructions", "validationStatus"]);

        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;

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
                    // AuthorSet, NoteSet, HistorySet — preserved verbatim.
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new Header(version, date, classification, classificationInstructions, validationStatus,
            preserved, preservedAttributes);
    }

    private static TelemetryMetaData ReadTelemetryMetaData(XmlReader reader, RecoveryContext? recovery = null, string path = "")
    {
        var parameterTypes = new List<ParameterTypeDefinition>();
        var parameters = new List<Parameter>();
        List<SequenceContainer>? containers = null;
        MessageSet? messageSet = null;
        List<RawXmlFragment>? preservedTypes = null;
        List<RawXmlFragment>? preservedParameters = null;
        List<RawXmlFragment>? preservedContainers = null;
        List<RawXmlFragment>? preserved = null;
        List<Algorithm>? algorithms = null;
        List<RawXmlFragment>? preservedAlgorithms = null;
        List<StreamDefinition>? streams = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return new TelemetryMetaData(parameterTypes, parameters);
        }

        reader.ReadStartElement();

        List<string>? pendingComments = null;
        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                DrainComments(ref preserved, ref pendingComments, reader.LocalName);
            }

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ParameterTypeSet")
            {
                ReadParameterTypeSet(reader, parameterTypes, ref preservedTypes, recovery, path);
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ParameterSet")
            {
                ReadParameterSet(reader, parameters, ref preservedParameters, recovery, path);
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ContainerSet")
            {
                containers = ReadContainerSet(reader, ref preservedContainers, recovery, path);
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "MessageSet")
            {
                messageSet = ReadMessageSet(reader, recovery, path);
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "AlgorithmSet" && algorithms is null)
            {
                algorithms = new List<Algorithm>();
                ReadAlgorithmSet(reader, algorithms, ref preservedAlgorithms);
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "StreamSet" && streams is null)
            {
                streams = new List<StreamDefinition>();
                ReadStreamSet(reader, streams);
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                Preserve(ref preserved, reader);
            }
            else if (!TryCaptureComment(reader, ref pendingComments))
            {
                reader.Read();
            }
        }

        DrainComments(ref preserved, ref pendingComments, null);
        reader.ReadEndElement();

        return new TelemetryMetaData(
            parameterTypes, parameters, preservedTypes, preservedParameters, preserved, containers, messageSet,
            preservedContainers, algorithms, preservedAlgorithms, streams);
    }

    private static readonly IReadOnlyDictionary<string, StreamKind> StreamElementKinds =
        new Dictionary<string, StreamKind>
        {
            ["FixedFrameStream"] = StreamKind.FixedFrame,
            ["VariableFrameStream"] = StreamKind.VariableFrame,
            ["CustomStream"] = StreamKind.Custom,
        };

    private static void ReadStreamSet(XmlReader reader, List<StreamDefinition> streams)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element
                && StreamElementKinds.TryGetValue(reader.LocalName, out var kind)
                && reader.GetAttribute("name") is { } name)
            {
                streams.Add(ReadStream(reader, name, kind));
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                var elementName = reader.LocalName;
                streams.Add(new StreamDefinition(elementName, StreamKind.Custom,
                    RawXml: new RawXmlFragment(elementName, reader.ReadOuterXml())));
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();
    }

    private static StreamDefinition ReadStream(XmlReader reader, string name, StreamKind kind)
    {
        var frameLengthInBits = reader.GetAttribute("frameLengthInBits");
        var bitRateInBps = reader.GetAttribute("bitRateInBPS");
        var preservedAttributes = CapturePreservedAttributes(reader, ["name", "frameLengthInBits", "bitRateInBPS"]);

        string? containerRef = null;
        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;
        Description? description = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element
                    && TryReadDescriptionChild(reader, ref description, ref preserved, ref pendingComments))
                {
                    // description-trio child handled
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ContainerRef"
                         && containerRef is null && reader.GetAttribute("containerRef") is not null
                         && reader.IsEmptyElement)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    containerRef = reader.GetAttribute("containerRef");
                    reader.Read();
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // SyncStrategy, ServiceRef, StreamRef, encodings — preserved verbatim.
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new StreamDefinition(name, kind, containerRef, frameLengthInBits, bitRateInBps,
            preserved, preservedAttributes, description);
    }

    private static void ReadAlgorithmSet(XmlReader reader, List<Algorithm> algorithms, ref List<RawXmlFragment>? preservedAlgorithms)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "CustomAlgorithm")
            {
                algorithms.Add(ReadAlgorithm(reader, AlgorithmKind.Custom));
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "MathAlgorithm")
            {
                algorithms.Add(ReadAlgorithm(reader, AlgorithmKind.Math));
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                Preserve(ref preservedAlgorithms, reader);
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();
    }

    private static Algorithm ReadAlgorithm(XmlReader reader, AlgorithmKind kind)
    {
        var name = RequireAttribute(reader, "name", "an algorithm");
        bool? thread = null;
        string? triggerContainer = null;
        long? priority = null;
        string[] modeledAttributes = ["name"];
        if (kind == AlgorithmKind.Custom)
        {
            thread = ParseBool(reader, "thread");
            triggerContainer = reader.GetAttribute("triggerContainer");
            priority = ParseLong(reader, "priority");
            modeledAttributes = ["name", "thread", "triggerContainer", "priority"];
        }
        var preservedAttributes = CapturePreservedAttributes(reader, modeledAttributes);

        string? algorithmText = null;
        string? language = null;
        List<AlgorithmParameterRef>? inputs = null;
        List<RawXmlFragment>? preservedInputs = null;
        List<AlgorithmParameterRef>? outputs = null;
        List<RawXmlFragment>? preservedOutputs = null;
        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;
        Description? description = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "AlgorithmText"
                    && algorithmText is null && HasOnlyAttributes(reader, "language"))
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    // Schema-invalid element children (or embedded comments) make the
                    // text unmodelable — the whole element then stays a preserved
                    // fragment instead, so nothing is dropped.
                    var textLanguage = reader.GetAttribute("language");
                    var outerXml = reader.ReadOuterXml();
                    if (TryReadTextOnlyElement(outerXml, out var text))
                    {
                        language = textLanguage;
                        algorithmText = text;
                    }
                    else
                    {
                        (preserved ??= new List<RawXmlFragment>()).Add(new RawXmlFragment("AlgorithmText", outerXml));
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "InputSet" && inputs is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    inputs = new List<AlgorithmParameterRef>();
                    ReadAlgorithmRefSet(reader, "InputParameterInstanceRef", "inputName", inputs, ref preservedInputs);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "OutputSet" && outputs is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    outputs = new List<AlgorithmParameterRef>();
                    ReadAlgorithmRefSet(reader, "OutputParameterRef", "outputName", outputs, ref preservedOutputs);
                }
                else if (reader.NodeType == XmlNodeType.Element
                         && TryReadDescriptionChild(reader, ref description, ref preserved, ref pendingComments))
                {
                    // description-trio child handled
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // ExternalAlgorithmSet, TriggerSet, MathOperation — preserved verbatim.
                    // (An AlgorithmText with unexpected attributes also lands here so
                    // nothing is dropped.)
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new Algorithm(name, kind, algorithmText, language, inputs, preservedInputs, outputs, preservedOutputs,
            thread, triggerContainer, priority, preserved, preservedAttributes, description);
    }

    /// <summary>
    /// Handles one NameDescription-trio child (issue #113): LongDescription (text-only,
    /// otherwise the fragment is preserved on the construct), AliasSet, and
    /// AncillaryDataSet. Returns false when the current element is none of the three.
    /// </summary>
    private static bool TryReadDescriptionChild(
        XmlReader reader,
        ref Description? description,
        ref List<RawXmlFragment>? preserved,
        ref List<string>? pendingComments)
    {
        if (reader.LocalName == "LongDescription" && description?.LongDescription is null)
        {
            DrainComments(ref preserved, ref pendingComments, reader.LocalName);
            var outerXml = reader.ReadOuterXml();
            if (TryReadTextOnlyElement(outerXml, out var text))
            {
                description = (description ?? new Description()) with { LongDescription = text };
            }
            else
            {
                (preserved ??= new List<RawXmlFragment>()).Add(new RawXmlFragment("LongDescription", outerXml));
            }
            return true;
        }
        if (reader.LocalName == "AliasSet" && description?.Aliases is null && description?.PreservedAliases is null)
        {
            DrainComments(ref preserved, ref pendingComments, reader.LocalName);
            var aliases = new List<AliasEntry>();
            List<RawXmlFragment>? preservedAliases = null;
            ReadSetOfRows(reader, "Alias", element =>
            {
                var nameSpace = element.GetAttribute("nameSpace");
                var alias = element.GetAttribute("alias");
                if (nameSpace is null || alias is null)
                {
                    return false;
                }
                aliases.Add(new AliasEntry(nameSpace, alias,
                    CapturePreservedAttributes(element, ["nameSpace", "alias"])));
                return true;
            }, ref preservedAliases);
            description = (description ?? new Description()) with
            {
                Aliases = aliases,
                PreservedAliases = preservedAliases,
            };
            return true;
        }
        if (reader.LocalName == "AncillaryDataSet" && description?.AncillaryData is null
            && description?.PreservedAncillaryData is null)
        {
            DrainComments(ref preserved, ref pendingComments, reader.LocalName);
            var rows = new List<AncillaryDataEntry>();
            List<RawXmlFragment>? preservedRows = null;
            ReadAncillaryDataSet(reader, rows, ref preservedRows);
            description = (description ?? new Description()) with
            {
                AncillaryData = rows,
                PreservedAncillaryData = preservedRows,
            };
            return true;
        }
        return false;
    }

    /// <summary>Reads a set of empty-shaped rows; a row the callback refuses is preserved.</summary>
    private static void ReadSetOfRows(
        XmlReader reader,
        string rowElementName,
        Func<XmlReader, bool> tryReadRow,
        ref List<RawXmlFragment>? preservedRows)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == rowElementName
                && reader.IsEmptyElement && tryReadRow(reader))
            {
                reader.Read();
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                Preserve(ref preservedRows, reader);
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();
    }

    private static void ReadAncillaryDataSet(
        XmlReader reader, List<AncillaryDataEntry> rows, ref List<RawXmlFragment>? preservedRows)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "AncillaryData"
                && reader.GetAttribute("name") is { } name)
            {
                var mimeType = reader.GetAttribute("mimeType");
                var href = reader.GetAttribute("href");
                var preservedAttributes = CapturePreservedAttributes(reader, ["name", "mimeType", "href"]);
                var outerXml = reader.ReadOuterXml();
                if (TryReadTextOnlyElement(outerXml, out var value))
                {
                    rows.Add(new AncillaryDataEntry(name, value, mimeType, href, preservedAttributes));
                }
                else
                {
                    // Element content (e.g. a foreign-namespace payload) — preserved whole.
                    (preservedRows ??= new List<RawXmlFragment>()).Add(new RawXmlFragment("AncillaryData", outerXml));
                }
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                Preserve(ref preservedRows, reader);
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();
    }

    /// <summary>Extracts an element's pure text content; false when it holds elements or comments.</summary>
    private static bool TryReadTextOnlyElement(string outerXml, out string text)
    {
        text = "";
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var builder = new StringBuilder();
            var depth = 0;
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        depth++;
                        if (depth > 1)
                        {
                            return false;
                        }
                        if (reader.IsEmptyElement)
                        {
                            depth--;
                        }
                        break;
                    case XmlNodeType.EndElement:
                        depth--;
                        break;
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        builder.Append(reader.Value);
                        break;
                    case XmlNodeType.Comment:
                    case XmlNodeType.ProcessingInstruction:
                        return false;
                }
            }
            text = builder.ToString();
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>Whether the current element carries no attributes beyond the allowed ones.</summary>
    private static bool HasOnlyAttributes(XmlReader reader, params string[] allowed)
    {
        var clean = true;
        if (reader.MoveToFirstAttribute())
        {
            do
            {
                if (!allowed.Contains(reader.LocalName) && reader.Prefix != "xmlns" && reader.LocalName != "xmlns")
                {
                    clean = false;
                    break;
                }
            }
            while (reader.MoveToNextAttribute());
            reader.MoveToElement();
        }
        return clean;
    }

    private static void ReadAlgorithmRefSet(
        XmlReader reader,
        string entryElementName,
        string nameAttribute,
        List<AlgorithmParameterRef> entries,
        ref List<RawXmlFragment>? preservedEntries)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == entryElementName
                && reader.IsEmptyElement)
            {
                var parameterRef = RequireAttribute(reader, "parameterRef", $"a {entryElementName}");
                var localName = reader.GetAttribute(nameAttribute);
                var preservedAttributes = CapturePreservedAttributes(reader, ["parameterRef", nameAttribute]);
                entries.Add(new AlgorithmParameterRef(parameterRef, localName, preservedAttributes));
                reader.Read();
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // Constants, entries with children, foreign content — preserved and
                // re-emitted inside the set.
                Preserve(ref preservedEntries, reader);
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();
    }

    private static MessageSet ReadMessageSet(XmlReader reader, RecoveryContext? recovery = null, string path = "")
    {
        var preservedAttributes = CapturePreservedAttributes(reader);
        var messages = new List<Message>();
        List<RawXmlFragment>? preserved = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return new MessageSet(messages, preserved, preservedAttributes);
        }

        reader.ReadStartElement();

        List<string>? pendingComments = null;
        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Message")
            {
                var leading = TakeLeadingComments(ref pendingComments);
                if (recovery is null)
                {
                    messages.Add(ReadMessage(reader, leading));
                }
                else
                {
                    ReadItemWithRecovery(reader, recovery, $"{path}/MessageSet",
                        r => messages.Add(ReadMessage(r, leading)), ref preserved);
                }
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // OptionalNameDescriptionType children (LongDescription, AliasSet,
                // AncillaryDataSet) — preserved verbatim.
                DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                Preserve(ref preserved, reader);
            }
            else if (!TryCaptureComment(reader, ref pendingComments))
            {
                reader.Read();
            }
        }

        DrainComments(ref preserved, ref pendingComments, null);
        reader.ReadEndElement();

        return new MessageSet(messages, preserved, preservedAttributes);
    }

    private static Message ReadMessage(XmlReader reader, List<RawXmlFragment>? leadingComments = null)
    {
        var name = RequireAttribute(reader, "name", "a Message");
        var preservedAttributes = CapturePreservedAttributes(reader, "name");

        string? containerRef = null;
        MatchCriteria? matchCriteria = null;
        Description? description = null;
        var preserved = leadingComments;
        List<string>? pendingComments = null;

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
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                }

                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ContainerRef")
                {
                    containerRef = RequireAttribute(reader, "containerRef", "a Message's ContainerRef");
                    reader.Skip();
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "MatchCriteria"
                         && matchCriteria is null)
                {
                    matchCriteria = ReadMatchCriteria(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element
                         && TryReadDescriptionChild(reader, ref description, ref preserved, ref pendingComments))
                {
                    // description-trio child handled
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        if (containerRef is null)
        {
            throw new XtceParseException($"Message '{name}' is missing its required ContainerRef element.");
        }

        return new Message(name, containerRef, preserved, preservedAttributes, matchCriteria, description);
    }

    private static MatchCriteria ReadMatchCriteria(XmlReader reader)
    {
        var preservedAttributes = CapturePreservedAttributes(reader);

        Comparison? comparison = null;
        List<Comparison>? comparisonList = null;
        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Comparison"
                    && comparison is null && reader.GetAttribute("parameterRef") is not null
                    && reader.GetAttribute("value") is not null && reader.IsEmptyElement)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    comparison = ReadComparison(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ComparisonList"
                         && comparisonList is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    comparisonList = ReadComparisonList(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // BooleanExpression, CustomAlgorithm — preserved verbatim.
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new MatchCriteria(comparison, comparisonList, preserved, preservedAttributes);
    }

    private static List<SequenceContainer> ReadContainerSet(
        XmlReader reader, ref List<RawXmlFragment>? preservedContainers, RecoveryContext? recovery = null, string path = "")
    {
        var containers = new List<SequenceContainer>();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return containers;
        }

        reader.ReadStartElement();

        List<string>? pendingComments = null;
        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "SequenceContainer")
            {
                var leading = TakeLeadingComments(ref pendingComments);
                if (recovery is null)
                {
                    containers.Add(ReadSequenceContainer(reader, leading));
                }
                else
                {
                    ReadItemWithRecovery(reader, recovery, $"{path}/ContainerSet",
                        r => containers.Add(ReadSequenceContainer(r, leading)), ref preservedContainers);
                }
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // ContainerSetType's choice admits only SequenceContainer, so anything else
                // is schema-invalid input — skipping (not preserving) is acceptable under
                // the schema-valid-input premise the preservation guarantee targets.
                reader.Skip();
            }
            else if (!TryCaptureComment(reader, ref pendingComments))
            {
                reader.Read();
            }
        }

        // ContainerSet has no preserved list of its own; comments trailing the last
        // container ride on that container as Trailing fragments (emitted after its end
        // tag). A set holding only comments can't occur in schema-valid input — the XSD
        // requires at least one SequenceContainer.
        if (pendingComments is not null && containers.Count > 0)
        {
            var trailing = pendingComments
                .Select(text => new RawXmlFragment(CommentAnchor.ElementName, text, CommentAnchor.Trailing));
            containers[^1] = containers[^1] with { Preserved = [.. containers[^1].Preserved ?? [], .. trailing] };
        }

        reader.ReadEndElement();

        return containers;
    }

    private static SequenceContainer ReadSequenceContainer(XmlReader reader, List<RawXmlFragment>? leadingComments = null)
    {
        var name = RequireAttribute(reader, "name", "a SequenceContainer");
        var isAbstract = ParseBool(reader, "abstract");
        var preservedAttributes = CapturePreservedAttributes(reader, "name", "abstract");

        var entries = new List<SequenceEntry>();
        BaseContainer? baseContainer = null;
        var preserved = leadingComments;
        List<string>? pendingComments = null;
        Description? description = null;

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
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                }

                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "EntryList")
                {
                    ReadEntryList(reader, entries);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "BaseContainer")
                {
                    baseContainer = ReadBaseContainer(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element
                         && TryReadDescriptionChild(reader, ref description, ref preserved, ref pendingComments))
                {
                    // description-trio child handled
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // DefaultRateInStream, RateInStreamSet, BinaryEncoding — preserved verbatim.
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new SequenceContainer(name, entries, baseContainer, isAbstract, preserved, preservedAttributes, description);
    }

    private static void ReadEntryList(XmlReader reader, List<SequenceEntry> entries, bool commandEntries = false)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ParameterRefEntry")
            {
                entries.Add(ReadRefEntry(reader, SequenceEntryKind.ParameterRef, "parameterRef"));
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ContainerRefEntry")
            {
                entries.Add(ReadRefEntry(reader, SequenceEntryKind.ContainerRef, "containerRef"));
            }
            else if (commandEntries && reader.NodeType == XmlNodeType.Element && reader.LocalName == "ArgumentRefEntry")
            {
                entries.Add(ReadRefEntry(reader, SequenceEntryKind.ArgumentRef, "argumentRef"));
            }
            else if (commandEntries && reader.NodeType == XmlNodeType.Element && reader.LocalName == "FixedValueEntry")
            {
                entries.Add(ReadFixedValueEntry(reader));
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // The remaining entry kinds (segments, stream segments, indirect and array
                // refs) ride as Raw entries IN POSITION — entry order is the packet layout,
                // so unlike set-typed collections these cannot be appended at the end.
                var elementName = reader.LocalName;
                var outerXml = reader.ReadOuterXml();
                entries.Add(new SequenceEntry(SequenceEntryKind.Raw, RawXml: new RawXmlFragment(elementName, outerXml)));
            }
            else if (reader.NodeType == XmlNodeType.Comment)
            {
                // Entry order IS the packet layout, so comments keep their exact position
                // as pseudo-entries; the writer emits them back as comments in place.
                entries.Add(new SequenceEntry(
                    SequenceEntryKind.Raw,
                    RawXml: new RawXmlFragment(CommentAnchor.ElementName, reader.Value)));
                reader.Read();
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();
    }

    /// <summary>
    /// Handles one entry-mechanic child (issue #109): LocationInContainerInBits and
    /// RepeatEntry model their fixed shapes (dynamic forms fall back to preserved
    /// fragments); IncludeCondition always models compositionally. Returns false when the
    /// current element is none of the three.
    /// </summary>
    private static bool TryReadEntryMechanic(
        XmlReader reader,
        ref EntryLocation? location,
        ref EntryRepeat? repeat,
        ref MatchCriteria? includeCondition,
        ref List<RawXmlFragment>? preserved,
        ref List<string>? pendingComments)
    {
        if (reader.LocalName == "LocationInContainerInBits" && location is null)
        {
            DrainComments(ref preserved, ref pendingComments, reader.LocalName);
            var outerXml = reader.ReadOuterXml();
            if (TryParseEntryLocation(outerXml, out location))
            {
                return true;
            }
            (preserved ??= new List<RawXmlFragment>()).Add(new RawXmlFragment("LocationInContainerInBits", outerXml));
            return true;
        }
        if (reader.LocalName == "RepeatEntry" && repeat is null)
        {
            DrainComments(ref preserved, ref pendingComments, reader.LocalName);
            var outerXml = reader.ReadOuterXml();
            if (TryParseEntryRepeat(outerXml, out repeat))
            {
                return true;
            }
            (preserved ??= new List<RawXmlFragment>()).Add(new RawXmlFragment("RepeatEntry", outerXml));
            return true;
        }
        if (reader.LocalName == "IncludeCondition" && includeCondition is null)
        {
            DrainComments(ref preserved, ref pendingComments, reader.LocalName);
            includeCondition = ReadMatchCriteria(reader);
            return true;
        }
        return false;
    }

    private static bool TryParseEntryLocation(string outerXml, out EntryLocation? location)
    {
        location = null;
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            reader.MoveToContent();
            var referenceLocation = reader.GetAttribute("referenceLocation");
            var preservedAttributes = CapturePreservedAttributes(reader, ["referenceLocation"]);
            if (reader.IsEmptyElement)
            {
                return false; // IntegerValueType requires a value child
            }
            reader.ReadStartElement();
            long? fixedValue = null;
            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "FixedValue" && fixedValue is null)
                {
                    var text = reader.ReadElementContentAsString();
                    if (!long.TryParse(text, out var parsed))
                    {
                        return false;
                    }
                    fixedValue = parsed;
                }
                else if (reader.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                {
                    reader.Read();
                }
                else
                {
                    return false; // DynamicValue, DiscreteLookupList, comments — keep the fragment
                }
            }
            if (fixedValue is null)
            {
                return false;
            }
            location = new EntryLocation(fixedValue.Value, referenceLocation, preservedAttributes);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false; // FixedValue with element children
        }
    }

    private static bool TryParseEntryRepeat(string outerXml, out EntryRepeat? repeat)
    {
        repeat = null;
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            reader.MoveToContent();
            var preservedAttributes = CapturePreservedAttributes(reader);
            if (reader.IsEmptyElement)
            {
                return false;
            }
            reader.ReadStartElement();
            long? count = null;
            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Count" && count is null
                    && !reader.IsEmptyElement)
                {
                    reader.ReadStartElement();
                    while (reader.NodeType != XmlNodeType.EndElement)
                    {
                        if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "FixedValue" && count is null)
                        {
                            if (!long.TryParse(reader.ReadElementContentAsString(), out var parsed))
                            {
                                return false;
                            }
                            count = parsed;
                        }
                        else if (reader.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                        {
                            reader.Read();
                        }
                        else
                        {
                            return false;
                        }
                    }
                    reader.ReadEndElement();
                }
                else if (reader.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                {
                    reader.Read();
                }
                else
                {
                    return false; // Offset, dynamic counts, comments — keep the fragment
                }
            }
            if (count is null)
            {
                return false;
            }
            repeat = new EntryRepeat(count.Value, preservedAttributes);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static SequenceEntry ReadRefEntry(XmlReader reader, SequenceEntryKind kind, string refAttributeName)
    {
        var reference = RequireAttribute(reader, refAttributeName, $"a {reader.LocalName}");
        var preservedAttributes = CapturePreservedAttributes(reader, refAttributeName);

        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;
        EntryLocation? location = null;
        EntryRepeat? repeat = null;
        MatchCriteria? includeCondition = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element
                    && TryReadEntryMechanic(reader, ref location, ref repeat, ref includeCondition,
                        ref preserved, ref pendingComments))
                {
                    // handled — modeled or preserved by the helper
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // TimeAssociation, AncillaryDataSet — preserved verbatim.
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new SequenceEntry(kind, reference, null, preserved, preservedAttributes,
            Location: location, Repeat: repeat, IncludeCondition: includeCondition);
    }

    private static SequenceEntry ReadFixedValueEntry(XmlReader reader)
    {
        var binaryValue = RequireAttribute(reader, "binaryValue", "a FixedValueEntry");
        var sizeInBits = ParseLong(reader, "sizeInBits");
        var name = reader.GetAttribute("name");
        // An unparseable sizeInBits stays a preserved attribute rather than being dropped.
        var modeledAttributes = sizeInBits is null
            ? new[] { "binaryValue", "name" }
            : new[] { "binaryValue", "name", "sizeInBits" };
        var preservedAttributes = CapturePreservedAttributes(reader, modeledAttributes);

        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;
        EntryLocation? location = null;
        EntryRepeat? repeat = null;
        MatchCriteria? includeCondition = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element
                    && TryReadEntryMechanic(reader, ref location, ref repeat, ref includeCondition,
                        ref preserved, ref pendingComments))
                {
                    // handled — modeled or preserved by the helper
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new SequenceEntry(SequenceEntryKind.FixedValue, null, null, preserved, preservedAttributes,
            binaryValue, sizeInBits, name, location, repeat, includeCondition);
    }

    private static BaseContainer ReadBaseContainer(XmlReader reader)
    {
        var containerRef = RequireAttribute(reader, "containerRef", "a BaseContainer");
        RestrictionCriteria? restrictionCriteria = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "RestrictionCriteria")
                {
                    restrictionCriteria = ReadRestrictionCriteria(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // BaseContainerType admits only RestrictionCriteria — schema-invalid
                    // input, skipped under the schema-valid-input premise.
                    reader.Skip();
                }
                else
                {
                    reader.Read();
                }
            }

            reader.ReadEndElement();
        }

        return new BaseContainer(containerRef, restrictionCriteria);
    }

    private static RestrictionCriteria ReadRestrictionCriteria(XmlReader reader)
    {
        Comparison? comparison = null;
        List<Comparison>? comparisonList = null;
        string? nextContainerRef = null;
        RawXmlFragment? raw = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return new RestrictionCriteria();
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Comparison")
            {
                comparison = ReadComparison(reader);
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ComparisonList")
            {
                comparisonList = ReadComparisonList(reader);
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "NextContainer")
            {
                nextContainerRef = RequireAttribute(reader, "containerRef", "a NextContainer");
                reader.Skip();
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // BooleanExpression or CustomAlgorithm — carried verbatim as the criteria.
                var elementName = reader.LocalName;
                raw = new RawXmlFragment(elementName, reader.ReadOuterXml());
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();

        return new RestrictionCriteria(comparison, comparisonList, nextContainerRef, raw);
    }

    private static List<Comparison> ReadComparisonList(XmlReader reader)
    {
        var comparisons = new List<Comparison>();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return comparisons;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Comparison")
            {
                comparisons.Add(ReadComparison(reader));
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

        return comparisons;
    }

    private static Comparison ReadComparison(XmlReader reader)
    {
        var parameterRef = RequireAttribute(reader, "parameterRef", "a Comparison");
        var value = reader.GetAttribute("value")
            ?? throw new XtceParseException("a Comparison element is missing its required 'value' attribute.");
        var comparisonOperator = reader.GetAttribute("comparisonOperator");
        var instance = ParseLong(reader, "instance");
        var useCalibratedValue = ParseBool(reader, "useCalibratedValue");
        var preservedAttributes = CapturePreservedAttributes(
            reader, "parameterRef", "value", "comparisonOperator", "instance", "useCalibratedValue");

        reader.Skip();

        return new Comparison(parameterRef, value, comparisonOperator, instance, useCalibratedValue, preservedAttributes);
    }

    private static void ReadParameterTypeSet(
        XmlReader reader,
        List<ParameterTypeDefinition> parameterTypes,
        ref List<RawXmlFragment>? preservedTypes,
        RecoveryContext? recovery = null,
        string path = "")
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();

        List<string>? pendingComments = null;
        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element &&
                ParameterTypeElementKinds.TryGetValue(reader.LocalName, out var kind))
            {
                var leading = TakeLeadingComments(ref pendingComments);
                if (recovery is null)
                {
                    parameterTypes.Add(ReadParameterTypeDefinition(reader, kind, leading));
                }
                else
                {
                    ReadItemWithRecovery(reader, recovery, $"{path}/ParameterTypeSet",
                        r => parameterTypes.Add(ReadParameterTypeDefinition(r, kind, leading)), ref preservedTypes);
                }
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // All ten XSD kinds are modeled, so only foreign/unknown elements land
                // here — preserved verbatim. The set is XSD choice-unbounded, so
                // re-emitting these after the modeled entries stays schema-valid.
                DrainComments(ref preservedTypes, ref pendingComments, reader.LocalName);
                Preserve(ref preservedTypes, reader);
            }
            else if (!TryCaptureComment(reader, ref pendingComments))
            {
                reader.Read();
            }
        }

        DrainComments(ref preservedTypes, ref pendingComments, null);
        reader.ReadEndElement();
    }

    private static ParameterTypeDefinition ReadParameterTypeDefinition(
        XmlReader reader, ParameterTypeKind kind, List<RawXmlFragment>? leadingComments = null)
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

        var arrayTypeRef = kind == ParameterTypeKind.Array
            ? RequireAttribute(reader, "arrayTypeRef", "an ArrayParameterType")
            : null;
        List<Dimension>? dimensions = kind == ParameterTypeKind.Array ? new List<Dimension>() : null;
        List<Member>? members = kind == ParameterTypeKind.Aggregate ? new List<Member>() : null;

        var modeledAttributes = kind switch
        {
            ParameterTypeKind.Integer => new[] { "name", "initialValue", "signed", "sizeInBits" },
            ParameterTypeKind.Float => new[] { "name", "initialValue", "sizeInBits" },
            ParameterTypeKind.Boolean => new[] { "name", "initialValue", "oneStringValue", "zeroStringValue" },
            ParameterTypeKind.Array => new[] { "name", "initialValue", "arrayTypeRef" },
            _ => new[] { "name", "initialValue" },
        };
        var preservedAttributes = CapturePreservedAttributes(reader, modeledAttributes);

        var preserved = leadingComments;
        List<string>? pendingComments = null;
        DataEncoding? dataEncoding = null;
        TimeEncoding? timeEncoding = null;
        List<Unit>? unitSet = null;
        List<RawXmlFragment>? preservedUnits = null;
        NumericAlarm? defaultAlarm = null;
        List<ContextNumericAlarm>? contextAlarms = null;
        NonNumericAlarm? nonNumericAlarm = null;
        ReferenceTime? referenceTime = null;
        List<ContextNonNumericAlarm>? nonNumericContextAlarms = null;
        Description? description = null;

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
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                }

                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "EnumerationList" && enumerations is not null)
                {
                    enumerations.AddRange(ReadEnumerationList(reader));
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "DimensionList" && dimensions is not null)
                {
                    dimensions.AddRange(ReadDimensionList(reader));
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "MemberList" && members is not null)
                {
                    members.AddRange(ReadMemberList(reader));
                }
                else if (reader.NodeType == XmlNodeType.Element && dataEncoding is null
                         && DataEncodingElementKinds.TryGetValue(reader.LocalName, out var encodingKind))
                {
                    dataEncoding = ReadDataEncoding(reader, encodingKind);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Encoding"
                         && timeEncoding is null
                         && kind is ParameterTypeKind.AbsoluteTime or ParameterTypeKind.RelativeTime)
                {
                    timeEncoding = ReadTimeEncoding(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ReferenceTime"
                         && referenceTime is null && HasOnlyAttributes(reader)
                         && kind is ParameterTypeKind.AbsoluteTime or ParameterTypeKind.RelativeTime)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    var outerXml = reader.ReadOuterXml();
                    if (TryParseReferenceTime(outerXml, out referenceTime))
                    {
                        // modeled
                    }
                    else
                    {
                        (preserved ??= new List<RawXmlFragment>()).Add(
                            new RawXmlFragment("ReferenceTime", outerXml));
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "UnitSet" && unitSet is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    unitSet = new List<Unit>();
                    ReadUnitSet(reader, unitSet, ref preservedUnits);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "DefaultAlarm"
                         && defaultAlarm is null
                         && kind is ParameterTypeKind.Integer or ParameterTypeKind.Float)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    defaultAlarm = ReadNumericAlarm(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "DefaultAlarm"
                         && nonNumericAlarm is null
                         && kind is ParameterTypeKind.Enumerated or ParameterTypeKind.Boolean
                             or ParameterTypeKind.Binary or ParameterTypeKind.String)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    nonNumericAlarm = ReadNonNumericAlarm(reader, kind);
                }
                else if (reader.NodeType == XmlNodeType.Element
                         && reader.LocalName == (kind == ParameterTypeKind.Binary ? "BinaryContextAlarmList" : "ContextAlarmList")
                         && nonNumericContextAlarms is null && HasOnlyAttributes(reader)
                         && kind is ParameterTypeKind.Enumerated or ParameterTypeKind.Boolean
                             or ParameterTypeKind.Binary or ParameterTypeKind.String)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    var listElementName = reader.LocalName;
                    var outerXml = reader.ReadOuterXml();
                    if (TryParseNonNumericContextAlarmList(outerXml, kind, out var parsedEntries))
                    {
                        nonNumericContextAlarms = parsedEntries;
                    }
                    else
                    {
                        (preserved ??= new List<RawXmlFragment>()).Add(
                            new RawXmlFragment(listElementName, outerXml));
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ContextAlarmList"
                         && contextAlarms is null && HasOnlyAttributes(reader)
                         && kind is ParameterTypeKind.Integer or ParameterTypeKind.Float)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    var outerXml = reader.ReadOuterXml();
                    if (TryParseContextAlarmList(outerXml, out var parsedAlarms))
                    {
                        contextAlarms = parsedAlarms;
                    }
                    else
                    {
                        (preserved ??= new List<RawXmlFragment>()).Add(
                            new RawXmlFragment("ContextAlarmList", outerXml));
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element
                         && TryReadDescriptionChild(reader, ref description, ref preserved, ref pendingComments))
                {
                    // description-trio child handled (modeled or preserved by the helper)
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // UnitSet, alarms, ToString, ValidRange, SizeRangeInCharacters,
                    // LongDescription, AliasSet, time-type ReferenceTime, ... — not
                    // modeled; preserved verbatim. (A schema-invalid second encoding
                    // also lands here, keeping the round trip lossless.)
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
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
            preservedAttributes,
            arrayTypeRef,
            dimensions,
            members,
            dataEncoding,
            timeEncoding,
            unitSet,
            preservedUnits,
            defaultAlarm,
            description,
            contextAlarms,
            nonNumericAlarm,
            nonNumericContextAlarms,
            referenceTime);
    }

    private static NonNumericAlarm ReadNonNumericAlarm(XmlReader reader, ParameterTypeKind kind) =>
        ReadNonNumericAlarmCore(reader, kind, modelContextMatch: false, out _);

    private static NonNumericAlarm ReadNonNumericAlarmCore(
        XmlReader reader, ParameterTypeKind kind, bool modelContextMatch, out MatchCriteria? contextMatch)
    {
        contextMatch = null;
        var minViolations = ParseLong(reader, "minViolations");
        var defaultAlarmLevel = kind is ParameterTypeKind.Enumerated or ParameterTypeKind.String
            ? reader.GetAttribute("defaultAlarmLevel")
            : null;
        var preservedAttributes = CapturePreservedAttributes(reader,
            defaultAlarmLevel is null && kind is not (ParameterTypeKind.Enumerated or ParameterTypeKind.String)
                ? ["minViolations"]
                : ["minViolations", "defaultAlarmLevel"]);

        List<EnumerationAlarmLevel>? enumerationAlarms = null;
        List<StringAlarmLevel>? stringAlarms = null;
        AlarmConditions? conditions = null;
        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "EnumerationAlarmList"
                    && enumerationAlarms is null && kind == ParameterTypeKind.Enumerated
                    && HasOnlyAttributes(reader))
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    var outerXml = reader.ReadOuterXml();
                    if (TryParseAlarmLevelRows(outerXml, "EnumerationAlarm", "enumerationLabel",
                            out var rows))
                    {
                        enumerationAlarms = rows
                            .Select(r => new EnumerationAlarmLevel(r.Level, r.Value, r.PreservedAttributes))
                            .ToList();
                    }
                    else
                    {
                        (preserved ??= new List<RawXmlFragment>()).Add(
                            new RawXmlFragment("EnumerationAlarmList", outerXml));
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "StringAlarmList"
                         && stringAlarms is null && kind == ParameterTypeKind.String
                         && HasOnlyAttributes(reader))
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    var outerXml = reader.ReadOuterXml();
                    if (TryParseAlarmLevelRows(outerXml, "StringAlarm", "matchPattern", out var rows))
                    {
                        stringAlarms = rows
                            .Select(r => new StringAlarmLevel(r.Level, r.Value, r.PreservedAttributes))
                            .ToList();
                    }
                    else
                    {
                        (preserved ??= new List<RawXmlFragment>()).Add(
                            new RawXmlFragment("StringAlarmList", outerXml));
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "AlarmConditions"
                         && conditions is null && HasOnlyAttributes(reader))
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    conditions = ReadAlarmConditions(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ContextMatch"
                         && modelContextMatch && contextMatch is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    contextMatch = ReadMatchCriteria(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // CustomAlarm, AncillaryDataSet — preserved verbatim.
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new NonNumericAlarm(minViolations, defaultAlarmLevel, enumerationAlarms, stringAlarms,
            conditions, preserved, preservedAttributes);
    }

    /// <summary>
    /// Strict parse of an alarm-level row list (EnumerationAlarmList / StringAlarmList):
    /// rows must be empty elements carrying alarmLevel plus the value attribute; extra
    /// attributes ride along preserved. Comments or foreign elements bail the whole list.
    /// </summary>
    private static bool TryParseAlarmLevelRows(
        string outerXml, string rowElementName, string valueAttribute,
        out List<(string Level, string Value, IReadOnlyList<RawAttribute>? PreservedAttributes)> rows)
    {
        rows = new List<(string, string, IReadOnlyList<RawAttribute>?)>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            reader.MoveToContent();
            if (reader.IsEmptyElement)
            {
                return true;
            }
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == rowElementName
                    && reader.GetAttribute("alarmLevel") is { } level
                    && reader.GetAttribute(valueAttribute) is { } value
                    && reader.IsEmptyElement)
                {
                    var rowPreserved = CapturePreservedAttributes(reader, ["alarmLevel", valueAttribute]);
                    rows.Add((level, value, rowPreserved));
                    reader.Read();
                }
                else if (reader.NodeType is XmlNodeType.Element or XmlNodeType.Comment or XmlNodeType.ProcessingInstruction)
                {
                    return false;
                }
                else
                {
                    reader.Read();
                }
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Strict parse of a non-numeric context-alarm list; false preserves the whole list
    /// (comments or foreign elements between entries). Entry content never fails —
    /// unmodelable children stay preserved inside the alarm — so positions hold.
    /// </summary>
    private static bool TryParseNonNumericContextAlarmList(
        string outerXml, ParameterTypeKind kind, out List<ContextNonNumericAlarm> entries)
    {
        entries = new List<ContextNonNumericAlarm>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            reader.MoveToContent();
            if (reader.IsEmptyElement)
            {
                return true;
            }
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ContextAlarm")
                {
                    var alarm = ReadNonNumericAlarmCore(reader, kind, modelContextMatch: true, out var contextMatch);
                    entries.Add(new ContextNonNumericAlarm(alarm, contextMatch));
                }
                else if (reader.NodeType is XmlNodeType.Element or XmlNodeType.Comment or XmlNodeType.ProcessingInstruction)
                {
                    return false;
                }
                else
                {
                    reader.Read();
                }
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static AlarmConditions ReadAlarmConditions(XmlReader reader)
    {
        MatchCriteria? watch = null, warning = null, distress = null, critical = null, severe = null;
        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "WatchAlarm" && watch is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    watch = ReadMatchCriteria(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "WarningAlarm" && warning is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    warning = ReadMatchCriteria(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "DistressAlarm" && distress is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    distress = ReadMatchCriteria(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "CriticalAlarm" && critical is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    critical = ReadMatchCriteria(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "SevereAlarm" && severe is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    severe = ReadMatchCriteria(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new AlarmConditions(watch, warning, distress, critical, severe, preserved);
    }

    private static NumericAlarm ReadNumericAlarm(XmlReader reader) =>
        ReadNumericAlarmCore(reader, modelContextMatch: false, out _);

    private static NumericAlarm ReadNumericAlarmCore(
        XmlReader reader, bool modelContextMatch, out MatchCriteria? contextMatch)
    {
        contextMatch = null;
        var minViolations = ParseLong(reader, "minViolations");
        var preservedAttributes = CapturePreservedAttributes(reader, ["minViolations"]);

        string? rangeForm = null;
        var hasStaticRanges = false;
        IReadOnlyList<RawAttribute>? staticPreservedAttributes = null;
        var ranges = new Dictionary<string, AlarmRange>();
        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "StaticAlarmRanges" && !hasStaticRanges)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    var outerXml = reader.ReadOuterXml();
                    if (TryParseStaticAlarmRanges(outerXml, ranges, out rangeForm, out staticPreservedAttributes))
                    {
                        hasStaticRanges = true;
                    }
                    else
                    {
                        ranges.Clear(); // a partial parse must not leak modeled ranges
                        rangeForm = null;
                        staticPreservedAttributes = null;
                        (preserved ??= new List<RawXmlFragment>()).Add(new RawXmlFragment("StaticAlarmRanges", outerXml));
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ContextMatch"
                         && modelContextMatch && contextMatch is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    contextMatch = ReadMatchCriteria(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // ChangeAlarmRanges, AlarmMultiRanges, AlarmConditions, CustomAlarm,
                    // AncillaryDataSet — preserved verbatim.
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new NumericAlarm(
            minViolations,
            rangeForm,
            ranges.GetValueOrDefault("WatchRange"),
            ranges.GetValueOrDefault("WarningRange"),
            ranges.GetValueOrDefault("DistressRange"),
            ranges.GetValueOrDefault("CriticalRange"),
            ranges.GetValueOrDefault("SevereRange"),
            hasStaticRanges,
            staticPreservedAttributes,
            preserved,
            preservedAttributes);
    }

    /// <summary>
    /// Strict parse of a numeric ContextAlarmList; false means the caller preserves the
    /// whole list verbatim (comments or foreign elements between entries). Entry content
    /// itself never fails — unmodelable alarm children stay preserved INSIDE the entry —
    /// so entries keep their list position (first matching context wins).
    /// </summary>
    private static bool TryParseContextAlarmList(string outerXml, out List<ContextNumericAlarm> entries)
    {
        entries = new List<ContextNumericAlarm>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            reader.MoveToContent();
            if (reader.IsEmptyElement)
            {
                return true;
            }
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ContextAlarm")
                {
                    // Unknown attributes and unmodelable children stay preserved on the
                    // alarm record itself, so this never loses anything.
                    var alarm = ReadNumericAlarmCore(reader, modelContextMatch: true, out var contextMatch);
                    entries.Add(new ContextNumericAlarm(alarm, contextMatch));
                }
                else if (reader.NodeType is XmlNodeType.Element or XmlNodeType.Comment or XmlNodeType.ProcessingInstruction)
                {
                    return false;
                }
                else
                {
                    reader.Read();
                }
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool TryParseStaticAlarmRanges(
        string outerXml,
        Dictionary<string, AlarmRange> ranges,
        out string? rangeForm,
        out IReadOnlyList<RawAttribute>? preservedAttributes)
    {
        rangeForm = null;
        preservedAttributes = null;
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            reader.MoveToContent();
            rangeForm = reader.GetAttribute("rangeForm");
            preservedAttributes = CapturePreservedAttributes(reader, ["rangeForm"]);

            if (reader.IsEmptyElement)
            {
                return true;
            }
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element
                    && reader.LocalName is "WatchRange" or "WarningRange" or "DistressRange" or "CriticalRange" or "SevereRange"
                    && !ranges.ContainsKey(reader.LocalName))
                {
                    var elementName = reader.LocalName;
                    var range = new AlarmRange(
                        reader.GetAttribute("minInclusive"),
                        reader.GetAttribute("minExclusive"),
                        reader.GetAttribute("maxInclusive"),
                        reader.GetAttribute("maxExclusive"),
                        CapturePreservedAttributes(reader, ["minInclusive", "minExclusive", "maxInclusive", "maxExclusive"]));
                    if (!SkipEmptyShapedElement(reader))
                    {
                        return false;
                    }
                    ranges[elementName] = range;
                }
                else if (reader.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                {
                    reader.Read();
                }
                else
                {
                    return false; // AncillaryDataSet, duplicates, comments — bail to the fragment
                }
            }
            return true;
        }
        catch (XmlException)
        {
            ranges.Clear();
            return false;
        }
    }

    private static void ReadUnitSet(XmlReader reader, List<Unit> units, ref List<RawXmlFragment>? preservedUnits)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Unit")
            {
                var description = reader.GetAttribute("description");
                var power = reader.GetAttribute("power");
                var factor = reader.GetAttribute("factor");
                var form = reader.GetAttribute("form");
                var preservedAttributes = CapturePreservedAttributes(reader, ["description", "power", "factor", "form"]);
                var value = reader.ReadElementContentAsString();
                units.Add(new Unit(value, description, power, factor, form, preservedAttributes));
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // Foreign content inside a UnitSet (schema-invalid) — preserved and
                // re-emitted inside the written UnitSet.
                Preserve(ref preservedUnits, reader);
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();
    }

    private static TimeEncoding ReadTimeEncoding(XmlReader reader)
    {
        var units = reader.GetAttribute("units");
        var scale = reader.GetAttribute("scale");
        var offset = reader.GetAttribute("offset");
        var preservedAttributes = CapturePreservedAttributes(reader, ["units", "scale", "offset"]);

        DataEncoding? dataEncoding = null;
        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && dataEncoding is null
                    && DataEncodingElementKinds.TryGetValue(reader.LocalName, out var encodingKind))
                {
                    dataEncoding = ReadDataEncoding(reader, encodingKind);
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new TimeEncoding(units, scale, offset, dataEncoding, preserved, preservedAttributes);
    }

    /// <summary>
    /// Strict parse of a ReferenceTime: exactly one Epoch (text only) or one empty-shaped
    /// OffsetFrom. Comments, unknown children, or both halves fail the parse and the
    /// caller preserves the whole element.
    /// </summary>
    private static bool TryParseReferenceTime(string outerXml, out ReferenceTime? referenceTime)
    {
        referenceTime = null;
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            reader.MoveToContent();
            if (reader.IsEmptyElement)
            {
                return false; // the XSD requires the choice
            }
            reader.ReadStartElement();

            string? epoch = null;
            string? offsetRef = null;
            long? offsetInstance = null;
            bool? offsetUseCalibrated = null;
            IReadOnlyList<RawAttribute>? offsetPreserved = null;

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Epoch"
                    && epoch is null && offsetRef is null && !reader.IsEmptyElement
                    && reader.AttributeCount == 0)
                {
                    if (!TryReadTextOnlyElement(reader.ReadOuterXml(), out var text))
                    {
                        return false;
                    }
                    epoch = text.Trim();
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "OffsetFrom"
                         && offsetRef is null && epoch is null && reader.IsEmptyElement
                         && reader.GetAttribute("parameterRef") is { } parameterRef)
                {
                    offsetRef = parameterRef;
                    offsetInstance = ParseLong(reader, "instance");
                    offsetUseCalibrated = ParseBool(reader, "useCalibratedValue");
                    offsetPreserved = CapturePreservedAttributes(reader,
                        ["parameterRef", "instance", "useCalibratedValue"]);
                    reader.Read();
                }
                else if (reader.NodeType is XmlNodeType.Element or XmlNodeType.Comment or XmlNodeType.ProcessingInstruction)
                {
                    return false;
                }
                else
                {
                    reader.Read();
                }
            }

            if (epoch is null && offsetRef is null)
            {
                return false;
            }
            referenceTime = new ReferenceTime(epoch, offsetRef, offsetInstance, offsetUseCalibrated, offsetPreserved);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static DataEncoding ReadDataEncoding(XmlReader reader, DataEncodingKind kind)
    {
        var encoding = reader.GetAttribute("encoding");
        var sizeInBits = ParseLong(reader, "sizeInBits");
        var changeThreshold = reader.GetAttribute("changeThreshold");
        var bitOrder = reader.GetAttribute("bitOrder");
        var byteOrder = reader.GetAttribute("byteOrder");
        var preservedAttributes = CapturePreservedAttributes(reader,
            ["encoding", "sizeInBits", "changeThreshold", "bitOrder", "byteOrder"]);

        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;
        Calibrator? defaultCalibrator = null;
        List<ContextCalibrator>? contextCalibrators = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "DefaultCalibrator"
                    && defaultCalibrator is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    // A MathOperationCalibrator, embedded comments, or anything else
                    // unrecognizable keeps the whole element as a preserved fragment.
                    var outerXml = reader.ReadOuterXml();
                    if (TryParseDefaultCalibrator(outerXml, out var calibrator))
                    {
                        defaultCalibrator = calibrator;
                    }
                    else
                    {
                        (preserved ??= new List<RawXmlFragment>()).Add(new RawXmlFragment("DefaultCalibrator", outerXml));
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ContextCalibratorList"
                         && contextCalibrators is null && HasOnlyAttributes(reader))
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    var outerXml = reader.ReadOuterXml();
                    if (TryParseContextCalibratorList(outerXml, out var parsedEntries))
                    {
                        contextCalibrators = parsedEntries;
                    }
                    else
                    {
                        (preserved ??= new List<RawXmlFragment>()).Add(
                            new RawXmlFragment("ContextCalibratorList", outerXml));
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // ErrorDetectCorrect, the SizeInBits/Variable size shapes, transform
                    // algorithms — preserved verbatim.
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new DataEncoding(kind, encoding, sizeInBits, changeThreshold, bitOrder, byteOrder, preserved,
            preservedAttributes, defaultCalibrator, contextCalibrators);
    }

    /// <summary>
    /// Strict parse of a ContextCalibratorList; false means the caller preserves the
    /// whole list verbatim (comments or foreign elements between entries). An entry whose
    /// own content can't be modeled stays a RawXml entry in position — context
    /// calibrators are evaluated in list order.
    /// </summary>
    private static bool TryParseContextCalibratorList(string outerXml, out List<ContextCalibrator> entries)
    {
        entries = new List<ContextCalibrator>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            reader.MoveToContent();
            if (reader.IsEmptyElement)
            {
                return true;
            }
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ContextCalibrator")
                {
                    var entryXml = reader.ReadOuterXml();
                    entries.Add(TryParseContextCalibrator(entryXml, out var entry)
                        ? entry
                        : new ContextCalibrator(RawXml: new RawXmlFragment("ContextCalibrator", entryXml)));
                }
                else if (reader.NodeType is XmlNodeType.Element or XmlNodeType.Comment or XmlNodeType.ProcessingInstruction)
                {
                    return false;
                }
                else
                {
                    reader.Read();
                }
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool TryParseContextCalibrator(string outerXml, out ContextCalibrator entry)
    {
        entry = new ContextCalibrator();
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            reader.MoveToContent();
            if (!HasOnlyAttributes(reader) || reader.IsEmptyElement)
            {
                return false;
            }
            reader.ReadStartElement();

            MatchCriteria? context = null;
            Calibrator? calibrator = null;

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ContextMatch"
                    && context is null)
                {
                    // The same MatchCriteriaType shape as everywhere else — unmodelable
                    // match forms stay preserved INSIDE the criteria, so this never fails.
                    context = ReadMatchCriteria(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Calibrator"
                         && calibrator is null)
                {
                    if (!TryParseDefaultCalibrator(reader.ReadOuterXml(), out calibrator))
                    {
                        return false; // MathOperationCalibrator etc. — whole entry rides raw
                    }
                }
                else if (reader.NodeType is XmlNodeType.Element or XmlNodeType.Comment or XmlNodeType.ProcessingInstruction)
                {
                    return false;
                }
                else
                {
                    reader.Read();
                }
            }

            if (context is null || calibrator is null)
            {
                return false; // the XSD requires both halves
            }
            entry = new ContextCalibrator(context, calibrator);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool TryParseDefaultCalibrator(string outerXml, out Calibrator? calibrator)
    {
        calibrator = null;
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            reader.MoveToContent();
            if (reader.NodeType != XmlNodeType.Element || reader.IsEmptyElement)
            {
                return false;
            }
            reader.ReadStartElement();

            CalibratorKind? kind = null;
            long? splineOrder = null;
            bool? extrapolate = null;
            IReadOnlyList<RawAttribute>? preservedAttributes = null;
            List<PolynomialTerm>? terms = null;
            List<SplinePointEntry>? points = null;
            List<RawXmlFragment>? preservedChildren = null;

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && kind is null
                    && reader.LocalName is "PolynomialCalibrator" or "SplineCalibrator")
                {
                    kind = reader.LocalName == "PolynomialCalibrator" ? CalibratorKind.Polynomial : CalibratorKind.Spline;
                    string[] modeledAttributes = [];
                    if (kind == CalibratorKind.Spline)
                    {
                        splineOrder = ParseLong(reader, "order");
                        extrapolate = ParseBool(reader, "extrapolate");
                        modeledAttributes = ["order", "extrapolate"];
                    }
                    preservedAttributes = CapturePreservedAttributes(reader, modeledAttributes);

                    if (reader.IsEmptyElement)
                    {
                        reader.Read();
                        continue;
                    }
                    reader.ReadStartElement();
                    while (reader.NodeType != XmlNodeType.EndElement)
                    {
                        if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Term"
                            && kind == CalibratorKind.Polynomial)
                        {
                            var coefficient = reader.GetAttribute("coefficient");
                            var exponent = reader.GetAttribute("exponent");
                            var termPreserved = CapturePreservedAttributes(reader, ["coefficient", "exponent"]);
                            if (coefficient is null || exponent is null || !SkipEmptyShapedElement(reader))
                            {
                                return false;
                            }
                            (terms ??= new List<PolynomialTerm>()).Add(
                                new PolynomialTerm(coefficient, exponent, termPreserved));
                        }
                        else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "SplinePoint"
                                 && kind == CalibratorKind.Spline)
                        {
                            var raw = reader.GetAttribute("raw");
                            var calibrated = reader.GetAttribute("calibrated");
                            var pointOrder = reader.GetAttribute("order");
                            var pointPreserved = CapturePreservedAttributes(reader, ["raw", "calibrated", "order"]);
                            if (raw is null || calibrated is null || !SkipEmptyShapedElement(reader))
                            {
                                return false;
                            }
                            (points ??= new List<SplinePointEntry>()).Add(
                                new SplinePointEntry(raw, calibrated, pointOrder, pointPreserved));
                        }
                        else if (reader.NodeType == XmlNodeType.Element)
                        {
                            // AncillaryDataSet (or foreign content) — preserved on the record.
                            Preserve(ref preservedChildren, reader);
                        }
                        else if (reader.NodeType is XmlNodeType.Comment or XmlNodeType.ProcessingInstruction)
                        {
                            return false; // comment placement can't be modeled — bail to the fragment
                        }
                        else
                        {
                            reader.Read();
                        }
                    }
                    reader.ReadEndElement();
                }
                else if (reader.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                {
                    reader.Read();
                }
                else
                {
                    return false; // MathOperationCalibrator, a second calibrator, comments...
                }
            }

            if (kind is null)
            {
                return false;
            }
            calibrator = new Calibrator(kind.Value, terms, points, splineOrder, extrapolate,
                preservedChildren, preservedAttributes);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>Advances past an element that must have no content; false when it has any.</summary>
    private static bool SkipEmptyShapedElement(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return true;
        }
        reader.ReadStartElement();
        while (reader.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
        {
            reader.Read();
        }
        if (reader.NodeType != XmlNodeType.EndElement)
        {
            return false;
        }
        reader.ReadEndElement();
        return true;
    }

    private static List<Dimension> ReadDimensionList(XmlReader reader)
    {
        var dimensions = new List<Dimension>();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return dimensions;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Dimension")
            {
                dimensions.Add(ReadDimension(reader));
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

        return dimensions;
    }

    private static Dimension ReadDimension(XmlReader reader)
    {
        DimensionIndex starting = new();
        DimensionIndex ending = new();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return new Dimension(starting, ending);
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "StartingIndex" or "EndingIndex")
            {
                var isStarting = reader.LocalName == "StartingIndex";
                var index = ReadDimensionIndex(reader);
                if (isStarting)
                {
                    starting = index;
                }
                else
                {
                    ending = index;
                }
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

        return new Dimension(starting, ending);
    }

    private static DimensionIndex ReadDimensionIndex(XmlReader reader)
    {
        // IntegerValueType: FixedValue is modeled; DynamicValue / DiscreteLookupList ride
        // as preserved fragments.
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return new DimensionIndex();
        }

        long? fixedValue = null;
        RawXmlFragment? raw = null;

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "FixedValue")
            {
                var text = reader.ReadElementContentAsString();
                if (!long.TryParse(text, out var parsed))
                {
                    throw new XtceParseException($"FixedValue '{text}' is not a valid integer.");
                }
                fixedValue = parsed;
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                var elementName = reader.LocalName;
                raw = new RawXmlFragment(elementName, reader.ReadOuterXml());
            }
            else
            {
                reader.Read();
            }
        }

        reader.ReadEndElement();

        return new DimensionIndex(fixedValue, raw);
    }

    private static List<Member> ReadMemberList(XmlReader reader)
    {
        var members = new List<Member>();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return members;
        }

        reader.ReadStartElement();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Member")
            {
                var name = RequireAttribute(reader, "name", "a Member");
                var typeRef = RequireAttribute(reader, "typeRef", "a Member");
                var initialValue = reader.GetAttribute("initialValue");
                var preservedAttributes = CapturePreservedAttributes(reader, "name", "typeRef", "initialValue");

                List<RawXmlFragment>? preserved = null;
                List<string>? pendingComments = null;
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
                            DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                            Preserve(ref preserved, reader);
                        }
                        else if (!TryCaptureComment(reader, ref pendingComments))
                        {
                            reader.Read();
                        }
                    }
                    DrainComments(ref preserved, ref pendingComments, null);
                    reader.ReadEndElement();
                }

                members.Add(new Member(name, typeRef, initialValue, preserved, preservedAttributes));
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

        return members;
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
        ref List<RawXmlFragment>? preservedParameters,
        RecoveryContext? recovery = null,
        string path = "")
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();

        List<string>? pendingComments = null;
        while (reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Parameter")
            {
                var leading = TakeLeadingComments(ref pendingComments);
                if (recovery is null)
                {
                    parameters.Add(ReadParameter(reader, leading));
                }
                else
                {
                    ReadItemWithRecovery(reader, recovery, $"{path}/ParameterSet",
                        r => parameters.Add(ReadParameter(r, leading)), ref preservedParameters);
                }
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // ParameterRef (cross-subsystem parameter includes) — preserved verbatim.
                DrainComments(ref preservedParameters, ref pendingComments, reader.LocalName);
                Preserve(ref preservedParameters, reader);
            }
            else if (!TryCaptureComment(reader, ref pendingComments))
            {
                reader.Read();
            }
        }

        DrainComments(ref preservedParameters, ref pendingComments, null);
        reader.ReadEndElement();
    }

    private static Parameter ReadParameter(XmlReader reader, List<RawXmlFragment>? leadingComments = null)
    {
        var name = RequireAttribute(reader, "name", "a Parameter");
        var parameterTypeRef = RequireAttribute(reader, "parameterTypeRef", "a Parameter");
        var initialValue = reader.GetAttribute("initialValue");
        var preservedAttributes = CapturePreservedAttributes(reader, "name", "parameterTypeRef", "initialValue");

        var preserved = leadingComments;
        List<string>? pendingComments = null;
        ParameterProperties? properties = null;
        Description? description = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement();

            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ParameterProperties" && properties is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    properties = ReadParameterProperties(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element
                         && TryReadDescriptionChild(reader, ref description, ref preserved, ref pendingComments))
                {
                    // description-trio child handled (modeled or preserved by the helper)
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // LongDescription, AliasSet, AncillaryDataSet — preserved verbatim.
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new Parameter(name, parameterTypeRef, initialValue, preserved, preservedAttributes, properties, description);
    }

    private static ParameterProperties ReadParameterProperties(XmlReader reader)
    {
        var dataSource = reader.GetAttribute("dataSource");
        var readOnly = ParseBool(reader, "readOnly");
        var persistence = ParseBool(reader, "persistence");
        var preservedAttributes = CapturePreservedAttributes(reader, ["dataSource", "readOnly", "persistence"]);

        List<RawXmlFragment>? preserved = null;
        List<string>? pendingComments = null;

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
                    // SystemName, ValidityCondition, PhysicalAddressSet, TimeAssociation —
                    // preserved verbatim.
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    Preserve(ref preserved, reader);
                }
                else if (!TryCaptureComment(reader, ref pendingComments))
                {
                    reader.Read();
                }
            }

            DrainComments(ref preserved, ref pendingComments, null);
            reader.ReadEndElement();
        }

        return new ParameterProperties(dataSource, readOnly, persistence, preserved, preservedAttributes);
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

    // ---- comment preservation ----------------------------------------------
    // Comments between children are buffered as text, then converted to "#comment"
    // fragments once their placement is known: anchored to the next sibling's element name,
    // marked Leading for a set item they preceded (emitted before its start tag), or left
    // unanchored when they trailed every child. Comments inside preserved fragments never
    // reach this path — ReadOuterXml already keeps them verbatim.

    /// <summary>Consumes a comment node into the pending buffer; false if not on a comment.</summary>
    private static bool TryCaptureComment(XmlReader reader, ref List<string>? pendingComments)
    {
        if (reader.NodeType != XmlNodeType.Comment)
        {
            return false;
        }
        (pendingComments ??= new List<string>()).Add(reader.Value);
        reader.Read();
        return true;
    }

    /// <summary>Moves buffered comments into a preserved list, anchored to the next sibling's name (null = trailing).</summary>
    private static void DrainComments(ref List<RawXmlFragment>? preserved, ref List<string>? pendingComments, string? anchor)
    {
        if (pendingComments is null)
        {
            return;
        }
        preserved ??= new List<RawXmlFragment>();
        foreach (var text in pendingComments)
        {
            preserved.Add(new RawXmlFragment(CommentAnchor.ElementName, text, anchor));
        }
        pendingComments = null;
    }

    /// <summary>Buffered comments as Leading fragments for the set item about to be read.</summary>
    private static List<RawXmlFragment>? TakeLeadingComments(ref List<string>? pendingComments)
    {
        if (pendingComments is null)
        {
            return null;
        }
        var fragments = pendingComments
            .Select(text => new RawXmlFragment(CommentAnchor.ElementName, text, CommentAnchor.Leading))
            .ToList();
        pendingComments = null;
        return fragments;
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
    // would drop the attribute on save, silently altering the document.
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
