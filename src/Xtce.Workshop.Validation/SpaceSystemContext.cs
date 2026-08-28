using System.Xml;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// The name namespaces XTCE reference attributes resolve against. Parameter,
/// parameter-type, container, and meta-command names are separate spaces — a parameterRef
/// never matches a container name, and so on.
/// </summary>
public enum NamedItemKind
{
    Parameter,
    ParameterType,
    Container,
    MetaCommand,
    ArgumentType,
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
    public required IReadOnlyDictionary<string, SequenceContainer> ModeledContainers { get; init; }
    public required IReadOnlyDictionary<string, Parameter> ModeledParameters { get; init; }
    public required IReadOnlySet<string> MetaCommandNames { get; init; }
    public required IReadOnlyDictionary<string, MetaCommand> ModeledMetaCommands { get; init; }
    public required IReadOnlySet<string> ArgumentTypeNames { get; init; }
    public required IReadOnlyDictionary<string, ParameterTypeDefinition> ModeledArgumentTypes { get; init; }

    /// <summary>Inline MetaCommand/CommandContainer names → owning MetaCommand (rule R21).</summary>
    public required IReadOnlyDictionary<string, MetaCommand> InlineCommandContainerOwners { get; init; }

    public SpaceSystemContext Root => Parent?.Root ?? this;

    public IReadOnlySet<string> NamesOf(NamedItemKind kind) => kind switch
    {
        NamedItemKind.Parameter => ParameterNames,
        NamedItemKind.ParameterType => ParameterTypeNames,
        NamedItemKind.Container => ContainerNames,
        NamedItemKind.MetaCommand => MetaCommandNames,
        NamedItemKind.ArgumentType => ArgumentTypeNames,
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
        var modeledContainers = new Dictionary<string, SequenceContainer>();
        var modeledParameters = new Dictionary<string, Parameter>();
        var metaCommandNames = new HashSet<string>();
        var modeledMetaCommands = new Dictionary<string, MetaCommand>();
        var argumentTypeNames = new HashSet<string>();
        var modeledArgumentTypes = new Dictionary<string, ParameterTypeDefinition>();
        var inlineCommandContainerOwners = new Dictionary<string, MetaCommand>();

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
                modeledParameters[parameter.Name] = parameter;
            }
            foreach (var container in telemetry.ContainerSet ?? [])
            {
                containerNames.Add(container.Name);
                modeledContainers[container.Name] = container;
            }

            foreach (var fragment in telemetry.PreservedContainerEntries ?? [])
            {
                if (XmlFragmentInspector.RootAttribute(fragment.OuterXml, "name") is { } name)
                {
                    containerNames.Add(name);
                }
            }
            foreach (var fragment in telemetry.PreservedParameterTypes ?? [])
            {
                if (XmlFragmentInspector.RootAttribute(fragment.OuterXml, "name") is { } name)
                {
                    parameterTypeNames.Add(name);
                }
            }
            foreach (var fragment in telemetry.PreservedParameters ?? [])
            {
                // A ParameterRef entry includes a parameter defined elsewhere under its
                // last path segment as the locally visible name.
                if (XmlFragmentInspector.RootAttribute(fragment.OuterXml, "parameterRef") is { } reference)
                {
                    var lastSlash = reference.LastIndexOf('/');
                    parameterNames.Add(lastSlash < 0 ? reference : reference[(lastSlash + 1)..]);
                }
            }
        }

        foreach (var fragment in node.Preserved ?? [])
        {
            // Documents built by hand (tests) may still carry a whole CommandMetaData as
            // a fragment; documents from the reader model it (below).
            if (fragment.ElementName == "CommandMetaData")
            {
                ScanCommandMetaData(fragment.OuterXml, parameterNames, parameterTypeNames, containerNames);
            }
        }

        if (node.CommandMetaData is { } commandMetaData)
        {
            // The command side's own parameter/parameter-type sets share the telemetry
            // side's reference namespaces (issue #98 modeled them; the fragment scan
            // below still covers CommandContainerSet and hand-built documents).
            foreach (var type in commandMetaData.ParameterTypeSet ?? [])
            {
                parameterTypeNames.Add(type.Name);
                modeledTypes[type.Name] = type;
            }
            foreach (var fragment in commandMetaData.PreservedParameterTypes ?? [])
            {
                if (XmlFragmentInspector.RootAttribute(fragment.OuterXml, "name") is { } name)
                {
                    parameterTypeNames.Add(name);
                }
            }
            foreach (var parameter in commandMetaData.ParameterSet ?? [])
            {
                parameterNames.Add(parameter.Name);
                modeledParameters[parameter.Name] = parameter;
            }
            foreach (var fragment in commandMetaData.PreservedParameters ?? [])
            {
                if (XmlFragmentInspector.RootAttribute(fragment.OuterXml, "parameterRef") is { } reference)
                {
                    var lastSlash = reference.LastIndexOf('/');
                    parameterNames.Add(lastSlash < 0 ? reference : reference[(lastSlash + 1)..]);
                }
            }
            foreach (var argumentType in commandMetaData.ArgumentTypeSet ?? [])
            {
                argumentTypeNames.Add(argumentType.Name);
                modeledArgumentTypes[argumentType.Name] = argumentType;
            }
            foreach (var fragment in commandMetaData.PreservedArgumentTypes ?? [])
            {
                if (XmlFragmentInspector.RootAttribute(fragment.OuterXml, "name") is { } name)
                {
                    argumentTypeNames.Add(name);
                }
            }
            foreach (var metaCommand in commandMetaData.MetaCommands)
            {
                metaCommandNames.Add(metaCommand.Name);
                modeledMetaCommands[metaCommand.Name] = metaCommand;
                if (metaCommand.CommandContainer is { } inlineContainer)
                {
                    // Inline command containers are referencable containers (BaseContainer
                    // refs to them are legal), and R21 needs to know who owns them.
                    containerNames.Add(inlineContainer.Name);
                    inlineCommandContainerOwners[inlineContainer.Name] = metaCommand;
                }
            }
            foreach (var fragment in commandMetaData.PreservedEntries ?? [])
            {
                // BlockMetaCommand defines a name; MetaCommandRef includes one defined
                // elsewhere under its reference's last segment.
                if (fragment.ElementName == "BlockMetaCommand" &&
                    XmlFragmentInspector.RootAttribute(fragment.OuterXml, "name") is { } blockName)
                {
                    metaCommandNames.Add(blockName);
                }
                else if (fragment.ElementName == "MetaCommandRef" &&
                    XmlFragmentInspector.RootText(fragment.OuterXml) is { } reference)
                {
                    var lastSlash = reference.LastIndexOf('/');
                    metaCommandNames.Add(lastSlash < 0 ? reference : reference[(lastSlash + 1)..]);
                }
            }
            foreach (var fragment in commandMetaData.Preserved ?? [])
            {
                // The command side's own ParameterSet/ParameterTypeSet/CommandContainerSet
                // ride as whole fragments — their definitions still count.
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
            ModeledContainers = modeledContainers,
            ModeledParameters = modeledParameters,
            MetaCommandNames = metaCommandNames,
            ModeledMetaCommands = modeledMetaCommands,
            ArgumentTypeNames = argumentTypeNames,
            ModeledArgumentTypes = modeledArgumentTypes,
            InlineCommandContainerOwners = inlineCommandContainerOwners,
        };

        foreach (var child in node.Children)
        {
            childrenByName[child.Name] = Build(child, context, $"{path}/{child.Name}");
        }

        return context;
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
