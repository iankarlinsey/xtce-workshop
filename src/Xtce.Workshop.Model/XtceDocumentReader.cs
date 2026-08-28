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
        var preserved = leadingComments;
        List<string>? pendingComments = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return new SpaceSystem(name, children, telemetryMetaData, preserved, preservedAttributes, commandMetaData);
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
                // Unmodeled child (LongDescription, AliasSet, AncillaryDataSet, Header,
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

        return new SpaceSystem(name, children, telemetryMetaData, preserved, preservedAttributes, commandMetaData);
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
                ReadMetaCommandSet(reader, metaCommands, ref preservedEntries, recovery, path);
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
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // CommandContainerSet, StreamSet — whole fragments; their definitions
                // still feed the reference namespaces via SpaceSystemContext's scanning.
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
            parameterTypes, preservedParameterTypes, parameters, preservedParameters, algorithms, preservedAlgorithms);
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
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // MetaCommandRef, BlockMetaCommand — preserved in the set.
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
        List<RawXmlFragment>? executionVerifiers = null;
        List<RawXmlFragment>? completeVerifiers = null;
        List<RawXmlFragment>? preservedVerifiers = null;
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
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "VerifierSet")
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    ReadVerifierSet(reader, ref executionVerifiers, ref completeVerifiers, ref preservedVerifiers);
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
            executionVerifiers, completeVerifiers, preservedVerifiers, preserved, preservedAttributes,
            commandContainer, arguments, preservedArguments, argumentAssignments);
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

    private static CommandContainer ReadCommandContainer(XmlReader reader)
    {
        var name = RequireAttribute(reader, "name", "a CommandContainer");
        var preservedAttributes = CapturePreservedAttributes(reader, "name");

        string? baseContainerRef = null;
        List<RawXmlFragment>? basePreserved = null;
        List<RawXmlFragment>? preserved = null;
        List<SequenceEntry>? entryList = null;
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
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // Description children, BinaryEncoding, DefaultRateInStream —
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

        return new CommandContainer(name, baseContainerRef, basePreserved, preserved, preservedAttributes, entryList);
    }

    private static void ReadVerifierSet(
        XmlReader reader,
        ref List<RawXmlFragment>? executionVerifiers,
        ref List<RawXmlFragment>? completeVerifiers,
        ref List<RawXmlFragment>? preservedVerifiers)
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
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ExecutionVerifier")
            {
                DrainComments(ref preservedVerifiers, ref pendingComments, reader.LocalName);
                Preserve(ref executionVerifiers, reader);
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "CompleteVerifier")
            {
                DrainComments(ref preservedVerifiers, ref pendingComments, reader.LocalName);
                Preserve(ref completeVerifiers, reader);
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // The six 0..1 verifier kinds (TransferredToRange, SentFromRange, Received,
                // Accepted, Queued, Failed) — preserved.
                DrainComments(ref preservedVerifiers, ref pendingComments, reader.LocalName);
                Preserve(ref preservedVerifiers, reader);
            }
            else if (!TryCaptureComment(reader, ref pendingComments))
            {
                reader.Read();
            }
        }

        DrainComments(ref preservedVerifiers, ref pendingComments, null);
        reader.ReadEndElement();
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
            else if (reader.NodeType == XmlNodeType.Element)
            {
                // Unmodeled sibling (StreamSet) — preserved verbatim, re-emitted in XSD
                // sequence order on save.
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
            preservedContainers, algorithms, preservedAlgorithms);
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
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // ExternalAlgorithmSet, TriggerSet, MathOperation, description
                    // children — preserved verbatim. (An AlgorithmText with unexpected
                    // attributes also lands here so nothing is dropped.)
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
            thread, triggerContainer, priority, preserved, preservedAttributes);
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
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // MatchCriteria (required by the XSD, a whole expression language) and
                    // description children — preserved verbatim.
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

        return new Message(name, containerRef, preserved, preservedAttributes);
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
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // DefaultRateInStream, RateInStreamSet, BinaryEncoding, LongDescription,
                    // AliasSet, AncillaryDataSet — preserved verbatim.
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

        return new SequenceContainer(name, entries, baseContainer, isAbstract, preserved, preservedAttributes);
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

    private static SequenceEntry ReadRefEntry(XmlReader reader, SequenceEntryKind kind, string refAttributeName)
    {
        var reference = RequireAttribute(reader, refAttributeName, $"a {reader.LocalName}");
        var preservedAttributes = CapturePreservedAttributes(reader, refAttributeName);

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
                    // LocationInContainerInBits, RepeatEntry, IncludeCondition,
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

        return new SequenceEntry(kind, reference, null, preserved, preservedAttributes);
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

        return new SequenceEntry(SequenceEntryKind.FixedValue, null, null, preserved, preservedAttributes,
            binaryValue, sizeInBits, name);
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
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "UnitSet" && unitSet is null)
                {
                    DrainComments(ref preserved, ref pendingComments, reader.LocalName);
                    unitSet = new List<Unit>();
                    ReadUnitSet(reader, unitSet, ref preservedUnits);
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
            preservedUnits);
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
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    // ContextCalibratorList, ErrorDetectCorrect, the SizeInBits/Variable
                    // size shapes, transform algorithms — preserved verbatim.
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
            preservedAttributes, defaultCalibrator);
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

        return new Parameter(name, parameterTypeRef, initialValue, preserved, preservedAttributes, properties);
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
