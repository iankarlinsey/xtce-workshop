using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>One leaf entry in a computed packet layout.</summary>
public sealed record PacketLayoutRow(
    string Name,
    string Kind,
    string SourceContainer,
    long? OffsetInBits,
    long? SizeInBits,
    bool IsVariable,
    string? Note);

public sealed record PacketLayout(IReadOnlyList<PacketLayoutRow> Rows, long? TotalSizeInBits);

/// <summary>
/// Best-effort STATIC bit layout for a SequenceContainer (the packet visualizer, original
/// scope item #4). Walks the BaseContainer inheritance chain parent-first (the XSD's
/// append semantics), expands ContainerRefEntry recursively, and takes encoded sizes from
/// preserved encoding fragments. Deliberately best-effort: dynamic sizes, IncludeConditions,
/// and repeats make true layout a runtime question — an unknown size yields a null size and
/// makes every following offset null rather than guessing. Cycle-guarded on both chains.
/// </summary>
public static class PacketLayoutBuilder
{
    public static PacketLayout? Build(SpaceSystem root, IReadOnlyList<int> systemPath, string containerName)
    {
        var context = SpaceSystemContext.Build(root);
        foreach (var index in systemPath)
        {
            var child = context.Node.Children.ElementAtOrDefault(index);
            if (child is null || !context.ChildrenByName.TryGetValue(child.Name, out var childContext))
            {
                return null;
            }
            context = childContext;
        }

        if (!context.ModeledContainers.TryGetValue(containerName, out var container))
        {
            return null;
        }

        var rows = new List<PacketLayoutRow>();
        long? offset = 0;
        AppendContainer(context, container, new HashSet<SequenceContainer>(ReferenceEqualityComparer.Instance),
            rows, ref offset, viaInheritance: true);

        return new PacketLayout(rows, offset);
    }

    private static void AppendContainer(
        SpaceSystemContext context,
        SequenceContainer container,
        HashSet<SequenceContainer> visited,
        List<PacketLayoutRow> rows,
        ref long? offset,
        bool viaInheritance)
    {
        if (!visited.Add(container))
        {
            rows.Add(new PacketLayoutRow(container.Name, "cycle", container.Name, offset, null, false,
                "container inheritance/reference cycle — layout truncated"));
            offset = null;
            return;
        }

        // Parent entries come first: "the parent container's entries are placed before the
        // entries in the child container forming one entry list."
        if (viaInheritance && container.BaseContainer is { } baseContainer)
        {
            var parent = NameReferenceResolver.Resolve(context, baseContainer.ContainerRef, NamedItemKind.Container);
            if (parent.Container is { } parentContainer && parent.DefinedIn is { } parentScope)
            {
                AppendContainer(parentScope, parentContainer, visited, rows, ref offset, viaInheritance: true);
            }
            else if (parent.Found)
            {
                rows.Add(new PacketLayoutRow(baseContainer.ContainerRef, "opaque-base", container.Name, offset, null, false,
                    "base container exists but isn't statically inspectable"));
                offset = null;
            }
        }

        foreach (var entry in container.EntryList)
        {
            ApplyExplicitLocation(entry, ref offset);

            switch (entry.Kind)
            {
                case SequenceEntryKind.ParameterRef:
                    AppendParameterEntry(context, container, entry, rows, ref offset);
                    break;

                case SequenceEntryKind.ContainerRef:
                {
                    var included = NameReferenceResolver.Resolve(context, entry.Ref!, NamedItemKind.Container);
                    if (included.Container is { } inner && included.DefinedIn is { } innerScope)
                    {
                        // An included (not inherited) container contributes its own full
                        // layout, including its own inheritance chain.
                        AppendContainer(innerScope, inner, visited, rows, ref offset, viaInheritance: true);
                    }
                    else
                    {
                        rows.Add(new PacketLayoutRow(entry.Ref!, "container", container.Name, offset, null, false,
                            included.Found ? "included container isn't statically inspectable" : "unresolved reference"));
                        offset = null;
                    }
                    break;
                }

                case SequenceEntryKind.Raw:
                {
                    var fragment = entry.RawXml!;
                    if (fragment.ElementName == CommentAnchor.ElementName)
                    {
                        break; // a preserved XML comment riding in entry position — no bits
                    }
                    var sizeAttr = XmlFragmentInspector.RootAttribute(fragment.OuterXml, "sizeInBits");
                    long? size = long.TryParse(sizeAttr, out var parsed) ? parsed : null;
                    var label = XmlFragmentInspector.RootAttribute(fragment.OuterXml, "parameterRef")
                        ?? XmlFragmentInspector.RootAttribute(fragment.OuterXml, "containerRef")
                        ?? XmlFragmentInspector.RootAttribute(fragment.OuterXml, "streamRef")
                        ?? XmlFragmentInspector.RootAttribute(fragment.OuterXml, "binaryValue")
                        ?? fragment.ElementName;
                    rows.Add(new PacketLayoutRow(label, fragment.ElementName, container.Name, offset, size, false,
                        size is null ? "size not statically known" : null));
                    Advance(ref offset, size);
                    break;
                }
            }
        }
    }

    private static void AppendParameterEntry(
        SpaceSystemContext context,
        SequenceContainer container,
        SequenceEntry entry,
        List<PacketLayoutRow> rows,
        ref long? offset)
    {
        var parameterResolution = NameReferenceResolver.Resolve(context, entry.Ref!, NamedItemKind.Parameter);
        long? size = null;
        var variable = false;
        string? note = null;

        if (parameterResolution.Parameter is { } parameter && parameterResolution.DefinedIn is { } definedIn)
        {
            var typeResolution = NameReferenceResolver.Resolve(definedIn, parameter.ParameterTypeRef, NamedItemKind.ParameterType);
            if (typeResolution.ParameterType is { } type)
            {
                (size, variable) = EncodedSize(type);
                if (size is null)
                {
                    note = variable ? "variable-length encoding" : "no statically-known encoding";
                }
                else if (variable)
                {
                    note = "variable — size shown is the maximum";
                }
            }
            else
            {
                note = "parameter type isn't statically inspectable";
            }
        }
        else
        {
            note = parameterResolution.Found ? "parameter isn't statically inspectable" : "unresolved reference";
        }

        rows.Add(new PacketLayoutRow(entry.Ref!, "parameter", container.Name, offset, size, variable, note));
        Advance(ref offset, size);
    }

    /// <summary>Statically-known encoded size of a type, in bits (shared with the CSV exporter).</summary>
    internal static (long? Size, bool Variable) EncodedSize(ParameterTypeDefinition type)
    {
        if (type.DataEncoding is { } dataEncoding)
        {
            return XmlFragmentInspector.FindEncodedSize(dataEncoding);
        }
        // The time types' Encoding wrapper is still a preserved fragment (and documents
        // built by hand may carry a whole data encoding as one).
        foreach (var fragment in type.Preserved ?? [])
        {
            if (fragment.ElementName is "IntegerDataEncoding" or "FloatDataEncoding"
                or "StringDataEncoding" or "BinaryDataEncoding" or "Encoding")
            {
                return XmlFragmentInspector.FindEncodedSizeInBits(fragment.OuterXml);
            }
        }
        return (null, false);
    }

    private static void ApplyExplicitLocation(SequenceEntry entry, ref long? offset)
    {
        // A fixed containerStart location re-anchors the running offset; anything else
        // (containerEnd, nextEntry, dynamic) makes it unknown from here on.
        foreach (var fragment in entry.Preserved ?? [])
        {
            if (fragment.ElementName != "LocationInContainerInBits")
            {
                continue;
            }
            foreach (var info in XmlFragmentInspector.FindLocations(fragment.OuterXml))
            {
                if (info.ReferenceLocation == "containerStart" && info.FixedValue is { } start && start >= 0)
                {
                    offset = start;
                }
                else if (info.ReferenceLocation == "previousEntry" && info.FixedValue is { } delta)
                {
                    Advance(ref offset, delta);
                }
                else
                {
                    offset = null;
                }
            }
        }
    }

    private static void Advance(ref long? offset, long? size)
    {
        offset = offset is { } current && size is { } s ? current + s : null;
    }
}
