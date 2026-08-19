using System.Xml;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// The three name namespaces XTCE reference attributes resolve against. Parameter,
/// parameter-type, and container names are separate spaces — a parameterRef never matches
/// a container name, and so on.
/// </summary>
public enum NamedItemKind
{
    Parameter,
    ParameterType,
    Container,
}

/// <summary>
/// Validation-time index over one SpaceSystem node: parent link, slash-joined path, child
/// lookup, and the name sets for each reference namespace. Built once per document by
/// <see cref="Build"/> and shared by every rule.
///
/// Names contributed by PRESERVED (unmodeled) content are included: preserved parameter
/// type fragments (Binary/time/Array/Aggregate kinds) are scanned for their name attribute,
/// preserved ParameterRef set entries contribute the last segment of their parameterRef
/// (that's what "include a Parameter defined in another sub-system" means locally), and
/// preserved CommandMetaData fragments are shallow-scanned for their ParameterSet /
/// ParameterTypeSet / CommandContainerSet definitions. Without this, a dangling-reference
/// rule would flag references to items that exist but aren't modeled — false positives an
/// error-severity rule cannot afford. ModeledParameterTypes tracks which parameter-type
/// names resolve to definitions rules can actually inspect (vs. opaque preserved ones).
/// </summary>
public sealed class SpaceSystemContext
{
    public required SpaceSystem Node { get; init; }
    public SpaceSystemContext? Parent { get; init; }
    public required string Path { get; init; }
    public required IReadOnlyDictionary<string, SpaceSystemContext> ChildrenByName { get; init; }
    public required IReadOnlySet<string> ParameterNames { get; init; }
    public required IReadOnlySet<string> ParameterTypeNames { get; init; }
    public required IReadOnlySet<string> ContainerNames { get; init; }
    public required IReadOnlyDictionary<string, ParameterTypeDefinition> ModeledParameterTypes { get; init; }

    public SpaceSystemContext Root => Parent?.Root ?? this;

    public IReadOnlySet<string> NamesOf(NamedItemKind kind) => kind switch
    {
        NamedItemKind.Parameter => ParameterNames,
        NamedItemKind.ParameterType => ParameterTypeNames,
        NamedItemKind.Container => ContainerNames,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public IEnumerable<SpaceSystemContext> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in ChildrenByName.Values)
        {
            foreach (var descendant in child.SelfAndDescendants())
            {
                yield return descendant;
            }
        }
    }

    public static SpaceSystemContext Build(SpaceSystem root) => Build(root, null, root.Name);

    private static SpaceSystemContext Build(SpaceSystem node, SpaceSystemContext? parent, string path)
    {
        var parameterNames = new HashSet<string>();
        var parameterTypeNames = new HashSet<string>();
        var containerNames = new HashSet<string>();
        var modeledTypes = new Dictionary<string, ParameterTypeDefinition>();

        if (node.TelemetryMetaData is { } telemetry)
        {
            foreach (var type in telemetry.ParameterTypeSet)
            {
                parameterTypeNames.Add(type.Name);
                modeledTypes[type.Name] = type;
            }
            foreach (var parameter in telemetry.ParameterSet)
            {
                parameterNames.Add(parameter.Name);
            }
            foreach (var container in telemetry.ContainerSet ?? [])
            {
                containerNames.Add(container.Name);
            }

            foreach (var fragment in telemetry.PreservedParameterTypes ?? [])
            {
                if (RootAttribute(fragment.OuterXml, "name") is { } name)
                {
                    parameterTypeNames.Add(name);
                }
            }
            foreach (var fragment in telemetry.PreservedParameters ?? [])
            {
                // A ParameterRef entry includes a parameter defined elsewhere under its
                // last path segment as the locally visible name.
                if (RootAttribute(fragment.OuterXml, "parameterRef") is { } reference)
                {
                    var lastSlash = reference.LastIndexOf('/');
                    parameterNames.Add(lastSlash < 0 ? reference : reference[(lastSlash + 1)..]);
                }
            }
        }

        foreach (var fragment in node.Preserved ?? [])
        {
            if (fragment.ElementName == "CommandMetaData")
            {
                ScanCommandMetaData(fragment.OuterXml, parameterNames, parameterTypeNames, containerNames);
            }
        }

        // Two passes because the record is immutable and children need their parent link:
        // create the context with a mutable child dictionary it retains by reference.
        var childrenByName = new Dictionary<string, SpaceSystemContext>();
        var context = new SpaceSystemContext
        {
            Node = node,
            Parent = parent,
            Path = path,
            ChildrenByName = childrenByName,
            ParameterNames = parameterNames,
            ParameterTypeNames = parameterTypeNames,
            ContainerNames = containerNames,
            ModeledParameterTypes = modeledTypes,
        };

        foreach (var child in node.Children)
        {
            childrenByName[child.Name] = Build(child, context, $"{path}/{child.Name}");
        }

        return context;
    }

    /// <summary>Reads one attribute off a fragment's root element without full parsing.</summary>
    private static string? RootAttribute(string outerXml, string attributeName)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return reader.MoveToContent() == XmlNodeType.Element ? reader.GetAttribute(attributeName) : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Shallow scan of a preserved CommandMetaData fragment for the definitions that share
    /// namespaces with the telemetry side: CommandMetaData's own ParameterSet/Parameter and
    /// ParameterTypeSet entries, and CommandContainerSet/CommandContainer names. Depth is
    /// tracked so a MetaCommand's Argument names (a different namespace) are never swept in.
    /// </summary>
    private static void ScanCommandMetaData(
        string outerXml, HashSet<string> parameterNames, HashSet<string> parameterTypeNames, HashSet<string> containerNames)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

            var stack = new Stack<string>();
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    var parent = stack.Count > 0 ? stack.Peek() : "";
                    var name = reader.GetAttribute("name");
                    if (name is not null)
                    {
                        if (parent == "ParameterSet" && reader.LocalName == "Parameter")
                        {
                            parameterNames.Add(name);
                        }
                        else if (parent == "ParameterTypeSet")
                        {
                            parameterTypeNames.Add(name);
                        }
                        else if (parent == "CommandContainerSet" && reader.LocalName == "CommandContainer")
                        {
                            containerNames.Add(name);
                        }
                    }

                    if (!reader.IsEmptyElement)
                    {
                        stack.Push(reader.LocalName);
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement && stack.Count > 0)
                {
                    stack.Pop();
                }
            }
        }
        catch (XmlException)
        {
            // A malformed preserved fragment shouldn't take validation down — it simply
            // contributes no names.
        }
    }
}
