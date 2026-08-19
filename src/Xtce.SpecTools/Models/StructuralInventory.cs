namespace Xtce.SpecTools.Models;

public sealed record NamedNode(string Name, int Line);

public sealed record OccursConstraint(string? OwnerName, string? MinOccurs, string? MaxOccurs, int Line);

public sealed record PatternConstraint(string? OwnerName, string Value, int Line);

public sealed record EnumerationConstraint(string? OwnerName, string Value, int Line);

public sealed record StructuralInventory(
    string SourceFile,
    int TotalNodes,
    IReadOnlyList<NamedNode> Elements,
    IReadOnlyList<NamedNode> Attributes,
    IReadOnlyList<NamedNode> ComplexTypes,
    IReadOnlyList<NamedNode> SimpleTypes,
    IReadOnlyList<EnumerationConstraint> Enumerations,
    IReadOnlyList<PatternConstraint> Patterns,
    IReadOnlyList<OccursConstraint> OccursConstraints,
    IReadOnlyList<NamedNode> Keys,
    IReadOnlyList<NamedNode> KeyRefs,
    IReadOnlyList<NamedNode> Uniques,
    IReadOnlyList<NamedNode> RefTypedNodes);
