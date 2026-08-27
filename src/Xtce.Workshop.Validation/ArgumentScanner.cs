using System.Xml;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// Parses the command-side constructs that ride as preserved XML — ArgumentTypeSet,
/// ArgumentList, ArgumentAssignmentList, ParameterToSetList, and the comparison forms
/// inside constraints/verifiers — into lightweight records so the R05/R07/R15 rules can
/// evaluate the argument-side candidate sites without expanding the object
/// model. Argument types come back as synthetic ParameterTypeDefinitions because the
/// XSD's Argument data types mirror the parameter ones attribute-for-attribute, letting
/// the existing typed-value checker be reused unchanged.
///
/// Documented partial scope: argumentTypeRef resolution is unqualified-name only, local
/// system then ancestors (a path-qualified ref is skipped, never guessed); malformed
/// preserved XML contributes nothing rather than failing validation.
/// </summary>
public static class ArgumentScanner
{
    public sealed record ArgumentDecl(string Name, string TypeRef, string? InitialValue);

    public sealed record ArgumentAssignmentInfo(string ArgumentName, string ArgumentValue);

    public sealed record ParameterToSetInfo(string ParameterRef, string? NewValue);

    public enum ComparisonForm
    {
        /// <summary>ComparisonType: parameterRef + value attributes, no children (candidate #88's family).</summary>
        Plain,

        /// <summary>ArgumentComparisonType: value attribute + ParameterInstanceRef/ArgumentInstanceRef child (#34).</summary>
        InstanceRef,

        /// <summary>(Argument)ComparisonCheckType: a Condition with an instance-ref LHS and a Value child (#35/#85).</summary>
        ConditionValue,
    }

    public sealed record ComparisonInfo(string? ParameterRef, string? ArgumentRef, string Value, ComparisonForm Form);

    /// <summary>An argument declaration plus the SpaceSystem scope its type name resolves from.</summary>
    public sealed record ScopedArgument(ArgumentDecl Decl, SpaceSystemContext Scope);

    // ---- ArgumentTypeSet ---------------------------------------------------------------

    /// <summary>The XSD's ArgumentTypeSet element names (RelativeTimeAgumentType is the schema's own typo).</summary>
    private static readonly IReadOnlyDictionary<string, ParameterTypeKind> ArgumentTypeKinds =
        new Dictionary<string, ParameterTypeKind>
        {
            ["IntegerArgumentType"] = ParameterTypeKind.Integer,
            ["FloatArgumentType"] = ParameterTypeKind.Float,
            ["StringArgumentType"] = ParameterTypeKind.String,
            ["BooleanArgumentType"] = ParameterTypeKind.Boolean,
            ["EnumeratedArgumentType"] = ParameterTypeKind.Enumerated,
            ["BinaryArgumentType"] = ParameterTypeKind.Binary,
            ["RelativeTimeAgumentType"] = ParameterTypeKind.RelativeTime,
            ["AbsoluteTimeArgumentType"] = ParameterTypeKind.AbsoluteTime,
            ["ArrayArgumentType"] = ParameterTypeKind.Array,
            ["AggregateArgumentType"] = ParameterTypeKind.Aggregate,
        };

    // Preserved fragments are immutable for a document's lifetime, but they are raw XML
    // strings — re-parsing them on every lookup made R15 take MINUTES on command-heavy
    // files (issue #94: per-argument type resolution x full ArgumentTypeSet parse).
    // Identity-keyed memoization; the tables release entries with their documents.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CommandMetaData, IReadOnlyList<ParameterTypeDefinition>>
        ArgumentTypeCache = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CommandMetaData, Dictionary<string, ParameterTypeDefinition>>
        ArgumentTypeIndexCache = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MetaCommand, IReadOnlyList<ArgumentDecl>>
        ArgumentListCache = new();

    /// <summary>Synthetic types for every argument type declared in this node's ArgumentTypeSet.</summary>
    public static IReadOnlyList<ParameterTypeDefinition> ScanArgumentTypes(CommandMetaData? commandMetaData)
    {
        if (commandMetaData is not null && ArgumentTypeCache.TryGetValue(commandMetaData, out var cached))
        {
            return cached;
        }
        var scanned = ScanArgumentTypesUncached(commandMetaData);
        if (commandMetaData is not null)
        {
            ArgumentTypeCache.AddOrUpdate(commandMetaData, scanned);
        }
        return scanned;
    }

    private static IReadOnlyList<ParameterTypeDefinition> ScanArgumentTypesUncached(CommandMetaData? commandMetaData)
    {
        var typeSet = (commandMetaData?.Preserved ?? []).FirstOrDefault(f => f.ElementName == "ArgumentTypeSet");
        if (typeSet is null)
        {
            return [];
        }

        var types = new List<ParameterTypeDefinition>();
        foreach (var (elementName, outerXml) in ChildElements(typeSet.OuterXml))
        {
            if (!ArgumentTypeKinds.TryGetValue(elementName, out var kind))
            {
                continue;
            }
            var name = XmlFragmentInspector.RootAttribute(outerXml, "name");
            if (name is null)
            {
                continue;
            }

            types.Add(new ParameterTypeDefinition(
                name,
                kind,
                InitialValue: XmlFragmentInspector.RootAttribute(outerXml, "initialValue"),
                Signed: ParseBool(XmlFragmentInspector.RootAttribute(outerXml, "signed")),
                SizeInBits: ParseLong(XmlFragmentInspector.RootAttribute(outerXml, "sizeInBits")),
                OneStringValue: XmlFragmentInspector.RootAttribute(outerXml, "oneStringValue"),
                ZeroStringValue: XmlFragmentInspector.RootAttribute(outerXml, "zeroStringValue"),
                Enumerations: kind == ParameterTypeKind.Enumerated ? ScanEnumerations(outerXml) : null,
                ArrayTypeRef: XmlFragmentInspector.RootAttribute(outerXml, "arrayTypeRef"),
                Dimensions: kind == ParameterTypeKind.Array
                    ? XmlFragmentInspector.FindDimensions(outerXml)
                        .Select(d => new Dimension(new DimensionIndex(d.StartingFixed), new DimensionIndex(d.EndingFixed)))
                        .ToList()
                    : null));
        }
        return types;
    }

    /// <summary>
    /// Resolves an unqualified argumentTypeRef in the given scope, walking self then
    /// ancestors (mirroring NameReferenceResolver's unqualified rule). Path-qualified
    /// refs return null — out of the documented scope.
    /// </summary>
    public static ParameterTypeDefinition? ResolveArgumentType(SpaceSystemContext scope, string typeRef)
    {
        if (typeRef.Contains('/'))
        {
            return null;
        }
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Node.CommandMetaData is not { } commandMetaData)
            {
                continue;
            }
            var index = ArgumentTypeIndexCache.GetValue(commandMetaData, static cmd =>
            {
                var byName = new Dictionary<string, ParameterTypeDefinition>();
                foreach (var type in ScanArgumentTypes(cmd))
                {
                    byName.TryAdd(type.Name, type);
                }
                return byName;
            });
            if (index.TryGetValue(typeRef, out var match))
            {
                return match;
            }
        }
        return null;
    }

    // ---- ArgumentList / inheritance ------------------------------------------------------

    /// <summary>The arguments declared directly on this MetaCommand's ArgumentList.</summary>
    public static IReadOnlyList<ArgumentDecl> ScanArguments(MetaCommand metaCommand)
    {
        return ArgumentListCache.GetValue(metaCommand, static cmd => ScanArgumentsUncached(cmd));
    }

    private static IReadOnlyList<ArgumentDecl> ScanArgumentsUncached(MetaCommand metaCommand)
    {
        var argumentList = (metaCommand.Preserved ?? []).FirstOrDefault(f => f.ElementName == "ArgumentList");
        if (argumentList is null)
        {
            return [];
        }

        var declarations = new List<ArgumentDecl>();
        foreach (var (elementName, outerXml) in ChildElements(argumentList.OuterXml))
        {
            if (elementName != "Argument")
            {
                continue;
            }
            var name = XmlFragmentInspector.RootAttribute(outerXml, "name");
            var typeRef = XmlFragmentInspector.RootAttribute(outerXml, "argumentTypeRef");
            if (name is not null && typeRef is not null)
            {
                declarations.Add(new ArgumentDecl(name, typeRef, XmlFragmentInspector.RootAttribute(outerXml, "initialValue")));
            }
        }
        return declarations;
    }

    /// <summary>
    /// All arguments visible on a MetaCommand — its own plus those inherited along the
    /// BaseMetaCommand chain (cycle-guarded), each paired with the SpaceSystem scope the
    /// declaring command lives in so its argumentTypeRef resolves from the right place.
    /// </summary>
    public static IReadOnlyList<ScopedArgument> MergedArguments(SpaceSystemContext usageContext, MetaCommand metaCommand)
    {
        var merged = new List<ScopedArgument>();
        var visited = new HashSet<MetaCommand>(ReferenceEqualityComparer.Instance);
        var current = metaCommand;
        var scope = usageContext;

        while (current is not null && visited.Add(current))
        {
            merged.AddRange(ScanArguments(current).Select(d => new ScopedArgument(d, scope)));
            if (current.BaseMetaCommandRef is not { } baseRef)
            {
                break;
            }
            var resolution = NameReferenceResolver.Resolve(scope, baseRef, NamedItemKind.MetaCommand);
            current = resolution.MetaCommand;
            scope = resolution.DefinedIn ?? scope;
        }
        return merged;
    }

    // ---- ArgumentAssignmentList / ParameterToSetList -------------------------------------

    public static IReadOnlyList<ArgumentAssignmentInfo> ScanArgumentAssignments(MetaCommand metaCommand)
    {
        var assignments = new List<ArgumentAssignmentInfo>();
        foreach (var fragment in metaCommand.BaseMetaCommandPreserved ?? [])
        {
            if (fragment.ElementName != "ArgumentAssignmentList")
            {
                continue;
            }
            foreach (var (elementName, outerXml) in ChildElements(fragment.OuterXml))
            {
                if (elementName != "ArgumentAssignment")
                {
                    continue;
                }
                var argumentName = XmlFragmentInspector.RootAttribute(outerXml, "argumentName");
                var argumentValue = XmlFragmentInspector.RootAttribute(outerXml, "argumentValue");
                if (argumentName is not null && argumentValue is not null)
                {
                    assignments.Add(new ArgumentAssignmentInfo(argumentName, argumentValue));
                }
            }
        }
        return assignments;
    }

    public static IReadOnlyList<ParameterToSetInfo> ScanParameterToSets(MetaCommand metaCommand)
    {
        var list = (metaCommand.Preserved ?? []).FirstOrDefault(f => f.ElementName == "ParameterToSetList");
        if (list is null)
        {
            return [];
        }

        var results = new List<ParameterToSetInfo>();
        foreach (var (elementName, outerXml) in ChildElements(list.OuterXml))
        {
            if (elementName != "ParameterToSet")
            {
                continue;
            }
            var parameterRef = XmlFragmentInspector.RootAttribute(outerXml, "parameterRef");
            if (parameterRef is not null)
            {
                results.Add(new ParameterToSetInfo(parameterRef, ChildElementText(outerXml, "NewValue")));
            }
        }
        return results;
    }

    // ---- Comparison forms -----------------------------------------------------------------

    /// <summary>
    /// Every value-carrying comparison in a fragment, in all three XSD shapes: plain
    /// ComparisonType (parameterRef/value attributes), ArgumentComparisonType (value
    /// attribute + instance-ref child), and (Argument)ComparisonCheckType Conditions
    /// (instance-ref LHS + Value child). Conditions whose right-hand side is another
    /// instance ref carry no literal and are skipped.
    /// </summary>
    public static IReadOnlyList<ComparisonInfo> ScanComparisons(string outerXml)
    {
        var comparisons = new List<ComparisonInfo>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

            // ReadOuterXml() advances the reader itself, so the loop must not Read() again
            // right after a capture — that would skip an adjacent sibling.
            var more = reader.Read();
            while (more)
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    more = reader.Read();
                    continue;
                }

                if (reader.LocalName == "Comparison")
                {
                    var subtree = reader.ReadOuterXml();
                    if (XmlFragmentInspector.RootAttribute(subtree, "value") is { } value)
                    {
                        var (parameterRef, argumentRef) = FirstInstanceRef(subtree);
                        if (parameterRef is not null || argumentRef is not null)
                        {
                            comparisons.Add(new ComparisonInfo(parameterRef, argumentRef, value, ComparisonForm.InstanceRef));
                        }
                        else if (XmlFragmentInspector.RootAttribute(subtree, "parameterRef") is { } plainRef)
                        {
                            comparisons.Add(new ComparisonInfo(plainRef, null, value, ComparisonForm.Plain));
                        }
                    }
                    more = reader.NodeType != XmlNodeType.None;
                }
                else if (reader.LocalName == "Condition")
                {
                    var subtree = reader.ReadOuterXml();
                    // A null Value means the right-hand side is another instance ref — no literal to check.
                    if (ChildElementText(subtree, "Value") is { } value)
                    {
                        var (parameterRef, argumentRef) = FirstInstanceRef(subtree);
                        if (parameterRef is not null || argumentRef is not null)
                        {
                            comparisons.Add(new ComparisonInfo(parameterRef, argumentRef, value, ComparisonForm.ConditionValue));
                        }
                    }
                    more = reader.NodeType != XmlNodeType.None;
                }
                else
                {
                    more = reader.Read();
                }
            }
        }
        catch (XmlException)
        {
            // Malformed preserved content contributes nothing rather than failing validation.
        }
        return comparisons;
    }

    /// <summary>Every fragment belonging to one MetaCommand (constraints, verifiers, container internals).</summary>
    public static IEnumerable<RawXmlFragment> CommandFragments(MetaCommand metaCommand)
    {
        foreach (var fragment in metaCommand.Preserved ?? [])
        {
            yield return fragment;
        }
        foreach (var fragment in metaCommand.BaseMetaCommandPreserved ?? [])
        {
            yield return fragment;
        }
        foreach (var fragment in metaCommand.ExecutionVerifiers ?? [])
        {
            yield return fragment;
        }
        foreach (var fragment in metaCommand.CompleteVerifiers ?? [])
        {
            yield return fragment;
        }
        foreach (var fragment in metaCommand.PreservedVerifiers ?? [])
        {
            yield return fragment;
        }
        foreach (var fragment in metaCommand.CommandContainer?.Preserved ?? [])
        {
            yield return fragment;
        }
        foreach (var fragment in metaCommand.CommandContainer?.BaseContainerPreserved ?? [])
        {
            yield return fragment;
        }
    }

    // ---- shared parsing helpers -------------------------------------------------------------

    /// <summary>(elementName, outerXml) for each direct child element of the fragment's root.</summary>
    public static IReadOnlyList<(string ElementName, string OuterXml)> ChildElements(string outerXml)
    {
        var children = new List<(string, string)>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

            // ReadOuterXml() advances the reader itself — Read() right after it would skip
            // an adjacent sibling element.
            var more = reader.Read();
            while (more)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1)
                {
                    children.Add((reader.LocalName, reader.ReadOuterXml()));
                    more = reader.NodeType != XmlNodeType.None;
                }
                else
                {
                    more = reader.Read();
                }
            }
        }
        catch (XmlException)
        {
            // Malformed preserved content contributes nothing rather than failing validation.
        }
        return children;
    }

    /// <summary>Text content of the first direct child element with the given name, or null.</summary>
    public static string? ChildElementText(string outerXml, string elementName) =>
        ChildElements(outerXml).FirstOrDefault(c => c.ElementName == elementName) is { OuterXml: { } childXml }
        && childXml.Length > 0
            ? XmlFragmentInspector.RootText(childXml)
            : null;

    private static (string? ParameterRef, string? ArgumentRef) FirstInstanceRef(string outerXml)
    {
        foreach (var (elementName, childXml) in ChildElements(outerXml))
        {
            if (elementName == "ParameterInstanceRef")
            {
                return (XmlFragmentInspector.RootAttribute(childXml, "parameterRef"), null);
            }
            if (elementName == "ArgumentInstanceRef")
            {
                return (null, XmlFragmentInspector.RootAttribute(childXml, "argumentRef"));
            }
        }
        return (null, null);
    }

    private static IReadOnlyList<EnumerationEntry> ScanEnumerations(string outerXml)
    {
        var entries = new List<EnumerationEntry>();
        foreach (var (listName, listXml) in ChildElements(outerXml))
        {
            if (listName != "EnumerationList")
            {
                continue;
            }
            foreach (var (entryName, entryXml) in ChildElements(listXml))
            {
                if (entryName != "Enumeration")
                {
                    continue;
                }
                var label = XmlFragmentInspector.RootAttribute(entryXml, "label");
                if (label is not null)
                {
                    entries.Add(new EnumerationEntry(
                        ParseLong(XmlFragmentInspector.RootAttribute(entryXml, "value")) ?? 0,
                        label));
                }
            }
        }
        return entries;
    }

    private static bool? ParseBool(string? value) => value switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => null,
    };

    private static long? ParseLong(string? value) =>
        value is not null && long.TryParse(value, out var parsed) ? parsed : null;
}
