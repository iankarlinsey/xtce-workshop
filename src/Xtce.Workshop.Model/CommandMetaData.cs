namespace Xtce.Workshop.Model;

/// <summary>
/// A MetaCommand's inline CommandContainer: name, the BaseContainer reference
/// (inheritance wiring), and — since issue #97 — the EntryList with the command-side
/// entry kinds modeled (ArgumentRefEntry, FixedValueEntry, plus the shared
/// ParameterRefEntry/ContainerRefEntry; the rest ride as Raw entries in position).
/// EntryList is null only for a container that had none in the source (schema-invalid
/// but preserved as-was); an empty element is an empty, non-null list.
/// </summary>
public sealed record CommandContainer(
    string Name,
    string? BaseContainerRef = null,
    IReadOnlyList<RawXmlFragment>? BaseContainerPreserved = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null,
    IReadOnlyList<SequenceEntry>? EntryList = null)
{
    public bool Equals(CommandContainer? other) =>
        other is not null
        && Name == other.Name
        && BaseContainerRef == other.BaseContainerRef
        && Structural.ListEquals(BaseContainerPreserved, other.BaseContainerPreserved)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes)
        && Structural.ListEquals(EntryList, other.EntryList);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(BaseContainerRef);
        Structural.AddList(ref hash, BaseContainerPreserved);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        Structural.AddList(ref hash, EntryList);
        return hash.ToHashCode();
    }
}

/// <summary>
/// An Argument declared on a MetaCommand's ArgumentList: name, type reference, optional
/// initial value; unmodeled children (LongDescription, AliasSet, ...) preserved.
/// </summary>
public sealed record Argument(
    string Name,
    string ArgumentTypeRef,
    string? InitialValue = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(Argument? other) =>
        other is not null
        && Name == other.Name
        && ArgumentTypeRef == other.ArgumentTypeRef
        && InitialValue == other.InitialValue
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(ArgumentTypeRef);
        hash.Add(InitialValue);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>One ArgumentAssignment inside a BaseMetaCommand's ArgumentAssignmentList.</summary>
public sealed record ArgumentAssignment(string ArgumentName, string ArgumentValue);

/// <summary>
/// A MetaCommand in a CommandMetaData's MetaCommandSet — modeled just deeply enough for
/// verifier-inheritance validation (rule R12): name, abstract flag, the BaseMetaCommand
/// reference, and the verifier lists. ExecutionVerifiers/CompleteVerifiers are kept as raw
/// fragments because their identity for duplicate detection is (whitespace-normalized) XML
/// equality — modeling their internals buys nothing for that. PreservedVerifiers carries
/// the six 0..1 verifier kinds; BaseMetaCommandPreserved carries BaseMetaCommand's optional
/// ArgumentAssignmentList; Preserved carries every other MetaCommand child (ArgumentList,
/// CommandContainer, constraints, significance, interlock, ParameterToSetList, ...).
/// </summary>
public sealed record MetaCommand(
    string Name,
    bool? Abstract = null,
    string? BaseMetaCommandRef = null,
    IReadOnlyList<RawXmlFragment>? BaseMetaCommandPreserved = null,
    IReadOnlyList<RawXmlFragment>? ExecutionVerifiers = null,
    IReadOnlyList<RawXmlFragment>? CompleteVerifiers = null,
    IReadOnlyList<RawXmlFragment>? PreservedVerifiers = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null,
    CommandContainer? CommandContainer = null,
    IReadOnlyList<Argument>? Arguments = null,
    IReadOnlyList<RawXmlFragment>? PreservedArguments = null,
    IReadOnlyList<ArgumentAssignment>? ArgumentAssignments = null)
{
    public bool Equals(MetaCommand? other) =>
        other is not null
        && Name == other.Name
        && Abstract == other.Abstract
        && BaseMetaCommandRef == other.BaseMetaCommandRef
        && Structural.ListEquals(BaseMetaCommandPreserved, other.BaseMetaCommandPreserved)
        && Structural.ListEquals(ExecutionVerifiers, other.ExecutionVerifiers)
        && Structural.ListEquals(CompleteVerifiers, other.CompleteVerifiers)
        && Structural.ListEquals(PreservedVerifiers, other.PreservedVerifiers)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes)
        && Equals(CommandContainer, other.CommandContainer)
        && Structural.ListEquals(Arguments, other.Arguments)
        && Structural.ListEquals(PreservedArguments, other.PreservedArguments)
        && Structural.ListEquals(ArgumentAssignments, other.ArgumentAssignments);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Abstract);
        hash.Add(BaseMetaCommandRef);
        Structural.AddList(ref hash, BaseMetaCommandPreserved);
        Structural.AddList(ref hash, ExecutionVerifiers);
        Structural.AddList(ref hash, CompleteVerifiers);
        Structural.AddList(ref hash, PreservedVerifiers);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        hash.Add(CommandContainer);
        Structural.AddList(ref hash, Arguments);
        Structural.AddList(ref hash, PreservedArguments);
        Structural.AddList(ref hash, ArgumentAssignments);
        return hash.ToHashCode();
    }
}

/// <summary>
/// The CommandMetaData element of a SpaceSystem, modeled for MetaCommandSet access.
/// PreservedEntries holds MetaCommandSet's non-MetaCommand entries (MetaCommandRef,
/// BlockMetaCommand); Preserved holds CommandMetaData's other children as whole fragments
/// (ParameterTypeSet, ParameterSet, ArgumentTypeSet, CommandContainerSet, StreamSet,
/// AlgorithmSet), re-emitted in XSD sequence order. Definitions inside those fragments
/// still contribute to the reference namespaces via scanning — see SpaceSystemContext.
/// </summary>
public sealed record CommandMetaData(
    IReadOnlyList<MetaCommand> MetaCommands,
    IReadOnlyList<RawXmlFragment>? PreservedEntries = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<ParameterTypeDefinition>? ArgumentTypeSet = null,
    IReadOnlyList<RawXmlFragment>? PreservedArgumentTypes = null,
    IReadOnlyList<ParameterTypeDefinition>? ParameterTypeSet = null,
    IReadOnlyList<RawXmlFragment>? PreservedParameterTypes = null,
    IReadOnlyList<Parameter>? ParameterSet = null,
    IReadOnlyList<RawXmlFragment>? PreservedParameters = null,
    IReadOnlyList<Algorithm>? AlgorithmSet = null,
    IReadOnlyList<RawXmlFragment>? PreservedAlgorithms = null)
{
    public bool Equals(CommandMetaData? other) =>
        other is not null
        && MetaCommands.SequenceEqual(other.MetaCommands)
        && Structural.ListEquals(PreservedEntries, other.PreservedEntries)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(ArgumentTypeSet, other.ArgumentTypeSet)
        && Structural.ListEquals(PreservedArgumentTypes, other.PreservedArgumentTypes)
        && Structural.ListEquals(ParameterTypeSet, other.ParameterTypeSet)
        && Structural.ListEquals(PreservedParameterTypes, other.PreservedParameterTypes)
        && Structural.ListEquals(ParameterSet, other.ParameterSet)
        && Structural.ListEquals(PreservedParameters, other.PreservedParameters)
        && Structural.ListEquals(AlgorithmSet, other.AlgorithmSet)
        && Structural.ListEquals(PreservedAlgorithms, other.PreservedAlgorithms);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var metaCommand in MetaCommands)
            hash.Add(metaCommand);
        Structural.AddList(ref hash, PreservedEntries);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, ArgumentTypeSet);
        Structural.AddList(ref hash, PreservedArgumentTypes);
        Structural.AddList(ref hash, ParameterTypeSet);
        Structural.AddList(ref hash, PreservedParameterTypes);
        Structural.AddList(ref hash, ParameterSet);
        Structural.AddList(ref hash, PreservedParameters);
        Structural.AddList(ref hash, AlgorithmSet);
        Structural.AddList(ref hash, PreservedAlgorithms);
        return hash.ToHashCode();
    }
}
