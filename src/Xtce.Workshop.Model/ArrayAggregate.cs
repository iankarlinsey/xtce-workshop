namespace Xtce.Workshop.Model;

/// <summary>
/// One StartingIndex/EndingIndex value of a Dimension. IntegerValueType is a choice of
/// FixedValue | DynamicValue | DiscreteLookupList — a fixed value is modeled, anything
/// dynamic rides as a preserved fragment (Raw), mutually exclusive by construction.
/// </summary>
public sealed record DimensionIndex(long? FixedValue = null, RawXmlFragment? Raw = null);

/// <summary>One Dimension of an ArrayParameterType's DimensionList. Indexes are zero-based.</summary>
public sealed record Dimension(DimensionIndex StartingIndex, DimensionIndex EndingIndex);

/// <summary>
/// One Member of an AggregateParameterType's MemberList — a named field with a type
/// reference, C-struct style. TypeRef resolves in the parameter-type namespace;
/// InitialValue overrides the member type's own.
/// </summary>
public sealed record Member(
    string Name,
    string TypeRef,
    string? InitialValue = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null,
    Description? Description = null)
{
    public bool Equals(Member? other) =>
        other is not null
        && Name == other.Name
        && TypeRef == other.TypeRef
        && InitialValue == other.InitialValue
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes)
        && Equals(Description, other.Description);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(TypeRef);
        hash.Add(InitialValue);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        hash.Add(Description);
        return hash.ToHashCode();
    }
}
