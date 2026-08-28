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

        var rows = new List<PacketLayoutRow>();
        long? offset = 0;
        if (context.ModeledContainers.TryGetValue(containerName, out var container))
        {
            AppendContainer(context, container, new HashSet<SequenceContainer>(ReferenceEqualityComparer.Instance),
                rows, ref offset, viaInheritance: true);
            return new PacketLayout(rows, offset);
        }

        // A MetaCommand's inline CommandContainer lays out too (#97): FixedValueEntry
        // sizes are explicit, ArgumentRefEntry sizes come from the owning command's
        // merged argument declarations and their (modeled) argument-type encodings.
        if (context.InlineCommandContainerOwners.TryGetValue(containerName, out var owner)
            && owner.CommandContainer is { } commandContainer)
        {
            AppendCommandContainer(context, owner, commandContainer,
                new HashSet<CommandContainer>(ReferenceEqualityComparer.Instance), rows, ref offset);
            return new PacketLayout(rows, offset);
        }

        // Standalone CommandContainerSet containers (#111): no owning command, so
        // argument refs stay unresolved, but parameters and fixed values lay out.
        if (context.StandaloneCommandContainers.TryGetValue(containerName, out var standalone))
        {
            AppendCommandContainer(context, null, standalone,
                new HashSet<CommandContainer>(ReferenceEqualityComparer.Instance), rows, ref offset);
            return new PacketLayout(rows, offset);
        }

        return null;
    }

    private static void AppendCommandContainer(
        SpaceSystemContext context,
        MetaCommand? owner,
        CommandContainer container,
        HashSet<CommandContainer> visited,
        List<PacketLayoutRow> rows,
        ref long? offset)
    {
        if (!visited.Add(container))
        {
            rows.Add(new PacketLayoutRow(container.Name, "cycle", container.Name, offset, null, false,
                "container inheritance/reference cycle — layout truncated"));
            offset = null;
            return;
        }

        if (container.BaseContainerRef is { } baseRef)
        {
            AppendContainerByRef(context, owner, container.Name, baseRef, visited, rows, ref offset,
                "base container exists but isn't statically inspectable");
        }

        var mergedArguments = owner is null
            ? (IReadOnlyList<ModeledArguments.Scoped>)[]
            : ModeledArguments.Merged(context, owner);

        foreach (var entry in container.EntryList ?? [])
        {
            ApplyExplicitLocation(entry, ref offset);

            switch (entry.Kind)
            {
                case SequenceEntryKind.ParameterRef:
                    AppendParameterEntry(context, container.Name, entry, rows, ref offset);
                    break;

                case SequenceEntryKind.ContainerRef:
                    AppendContainerByRef(context, owner, container.Name, entry.Ref!, visited, rows, ref offset,
                        "included container isn't statically inspectable");
                    break;

                case SequenceEntryKind.ArgumentRef:
                {
                    long? size = null;
                    var variable = false;
                    string? note = null;
                    var argument = mergedArguments.FirstOrDefault(a => a.Decl.Name == entry.Ref);
                    if (argument is null)
                    {
                        note = "unresolved argument reference";
                    }
                    else if (ModeledArguments.ResolveType(argument.Scope, argument.Decl.ArgumentTypeRef) is { } type)
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
                        note = "argument type isn't statically inspectable";
                    }
                    ApplyRepeat(entry, ref size, ref note);
                    rows.Add(new PacketLayoutRow(entry.Ref!, "argument", container.Name, offset, size, variable, note));
                    Advance(ref offset, size);
                    break;
                }

                case SequenceEntryKind.FixedValue:
                {
                    var label = entry.Name ?? entry.BinaryValue ?? "FixedValueEntry";
                    var fixedSize = entry.SizeInBits;
                    string? fixedNote = fixedSize is null ? "size not statically known" : null;
                    ApplyRepeat(entry, ref fixedSize, ref fixedNote);
                    rows.Add(new PacketLayoutRow(label, "fixed", container.Name, offset, fixedSize, false, fixedNote));
                    Advance(ref offset, fixedSize);
                    break;
                }

                case SequenceEntryKind.Raw:
                    AppendRawEntry(container.Name, entry, rows, ref offset);
                    break;
            }
        }
    }

    /// <summary>
    /// Expands a containerRef that may name a telemetry SequenceContainer or another
    /// MetaCommand's inline CommandContainer; anything else gets an opaque row.
    /// </summary>
    private static void AppendContainerByRef(
        SpaceSystemContext context,
        MetaCommand? owner,
        string sourceContainer,
        string containerRef,
        HashSet<CommandContainer> visited,
        List<PacketLayoutRow> rows,
        ref long? offset,
        string opaqueNote)
    {
        var resolution = NameReferenceResolver.Resolve(context, containerRef, NamedItemKind.Container);
        if (resolution.Container is { } sequenceContainer && resolution.DefinedIn is { } scope)
        {
            AppendContainer(scope, sequenceContainer, new HashSet<SequenceContainer>(ReferenceEqualityComparer.Instance),
                rows, ref offset, viaInheritance: true);
            return;
        }
        var lastSlash = containerRef.LastIndexOf('/');
        var localName = lastSlash < 0 ? containerRef : containerRef[(lastSlash + 1)..];
        if (resolution.DefinedIn is { } definedIn
            && definedIn.InlineCommandContainerOwners.TryGetValue(localName, out var innerOwner)
            && innerOwner.CommandContainer is { } innerContainer)
        {
            AppendCommandContainer(definedIn, innerOwner, innerContainer, visited, rows, ref offset);
            return;
        }
        if (resolution.DefinedIn is { } standaloneScope
            && standaloneScope.StandaloneCommandContainers.TryGetValue(localName, out var standalone))
        {
            // Extending a standalone base keeps the CHILD's argument scope (owner).
            AppendCommandContainer(standaloneScope, owner, standalone, visited, rows, ref offset);
            return;
        }
        rows.Add(new PacketLayoutRow(containerRef, "container", sourceContainer, offset, null, false,
            resolution.Found ? opaqueNote : "unresolved reference"));
        offset = null;
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
                    AppendParameterEntry(context, container.Name, entry, rows, ref offset);
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
                    AppendRawEntry(container.Name, entry, rows, ref offset);
                    break;
            }
        }
    }

    private static void AppendRawEntry(string sourceContainer, SequenceEntry entry, List<PacketLayoutRow> rows, ref long? offset)
    {
        var fragment = entry.RawXml!;
        if (fragment.ElementName == CommentAnchor.ElementName)
        {
            return; // a preserved XML comment riding in entry position — no bits
        }
        var sizeAttr = XmlFragmentInspector.RootAttribute(fragment.OuterXml, "sizeInBits");
        long? size = long.TryParse(sizeAttr, out var parsed) ? parsed : null;
        var label = XmlFragmentInspector.RootAttribute(fragment.OuterXml, "parameterRef")
            ?? XmlFragmentInspector.RootAttribute(fragment.OuterXml, "containerRef")
            ?? XmlFragmentInspector.RootAttribute(fragment.OuterXml, "streamRef")
            ?? XmlFragmentInspector.RootAttribute(fragment.OuterXml, "binaryValue")
            ?? fragment.ElementName;
        rows.Add(new PacketLayoutRow(label, fragment.ElementName, sourceContainer, offset, size, false,
            size is null ? "size not statically known" : null));
        Advance(ref offset, size);
    }

    private static void AppendParameterEntry(
        SpaceSystemContext context,
        string sourceContainer,
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

        ApplyRepeat(entry, ref size, ref note);
        rows.Add(new PacketLayoutRow(entry.Ref!, "parameter", sourceContainer, offset, size, variable, note));
        Advance(ref offset, size);
    }

    /// <summary>A fixed RepeatEntry multiplies the entry's footprint; conditional entries get a note.</summary>
    private static void ApplyRepeat(SequenceEntry entry, ref long? size, ref string? note)
    {
        if (entry.Repeat is { FixedCount: > 1 } repeat && size is { } fixedSize)
        {
            size = fixedSize * repeat.FixedCount;
            note = note is null ? $"×{repeat.FixedCount} repeat" : $"{note}; ×{repeat.FixedCount} repeat";
        }
        if (entry.IncludeCondition is not null)
        {
            var conditional = "conditional (IncludeCondition)";
            note = note is null ? conditional : $"{note}; {conditional}";
        }
    }

    /// <summary>Statically-known encoded size of a type, in bits (shared with the CSV exporter).</summary>
    internal static (long? Size, bool Variable) EncodedSize(ParameterTypeDefinition type)
    {
        if (type.DataEncoding is { } dataEncoding)
        {
            return XmlFragmentInspector.FindEncodedSize(dataEncoding);
        }
        if (type.TimeEncoding is { } timeEncoding)
        {
            return timeEncoding.DataEncoding is { } inner
                ? XmlFragmentInspector.FindEncodedSize(inner)
                : (null, false);
        }
        // Documents built by hand (tests) may still carry a whole encoding as a fragment.
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
        if (entry.Location is { } modeled)
        {
            if (modeled.ReferenceLocation == "containerStart" && modeled.FixedValue >= 0)
            {
                offset = modeled.FixedValue;
            }
            else if (modeled.ReferenceLocation is null or "previousEntry")
            {
                Advance(ref offset, modeled.FixedValue);
            }
            else
            {
                offset = null;
            }
        }
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
