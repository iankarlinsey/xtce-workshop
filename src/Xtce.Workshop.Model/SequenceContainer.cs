namespace Xtce.Workshop.Model;

/// <summary>
/// Which EntryList element a SequenceEntry represents. ParameterRef and ContainerRef are
/// the modeled kinds; Raw carries any of the other five entry kinds (ParameterSegmentRefEntry,
/// ContainerSegmentRefEntry, StreamSegmentEntry, IndirectParameterRefEntry,
/// ArrayParameterRefEntry) verbatim, IN POSITION — entry order is the packet layout, so
/// unlike ParameterTypeSet (unordered set semantics, preserved entries appended at the end),
/// EntryList keeps unmodeled entries interleaved exactly where they appeared.
/// </summary>
public enum SequenceEntryKind
{
    ParameterRef,
    ContainerRef,
    Raw,
}

/// <summary>
/// One entry in a SequenceContainer's EntryList. For ParameterRef/ContainerRef kinds, Ref
/// is the parameterRef/containerRef attribute, Preserved holds unmodeled children
/// (LocationInContainerInBits, RepeatEntry, IncludeCondition, TimeAssociation,
/// AncillaryDataSet) and PreservedAttributes unmodeled attributes (shortDescription).
/// For Kind == Raw, RawXml is the whole entry element and the other fields are null.
/// </summary>
public sealed record SequenceEntry(
    SequenceEntryKind Kind,
    string? Ref = null,
    RawXmlFragment? RawXml = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(SequenceEntry? other) =>
        other is not null
        && Kind == other.Kind
        && Ref == other.Ref
        && Equals(RawXml, other.RawXml)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Ref);
        hash.Add(RawXml);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A ComparisonType: a parameter-instance-to-value test. ComparisonOperator stays null when
/// absent (XSD default "==" applied by consumers at check time, per the no-baked-defaults
/// convention from issue #23); same for Instance (default 0) and UseCalibratedValue
/// (default true), which come from the ParameterInstanceRefType base.
/// </summary>
public sealed record Comparison(
    string ParameterRef,
    string Value,
    string? ComparisonOperator = null,
    long? Instance = null,
    bool? UseCalibratedValue = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(Comparison? other) =>
        other is not null
        && ParameterRef == other.ParameterRef
        && Value == other.Value
        && ComparisonOperator == other.ComparisonOperator
        && Instance == other.Instance
        && UseCalibratedValue == other.UseCalibratedValue
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ParameterRef);
        hash.Add(Value);
        hash.Add(ComparisonOperator);
        hash.Add(Instance);
        hash.Add(UseCalibratedValue);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A BaseContainer's RestrictionCriteria. The XSD shape is subtler than it looks:
/// RestrictionCriteriaType EXTENDS MatchCriteriaType, so its content is (required choice of
/// Comparison | ComparisonList | BooleanExpression | CustomAlgorithm) FOLLOWED BY an
/// optional NextContainer — NextContainer is additive, not a fourth alternative, and a
/// RestrictionCriteria containing only NextContainer does not validate. Exactly one of
/// Comparison / ComparisonList / Raw (BooleanExpression or CustomAlgorithm, carried
/// verbatim) is set, plus optionally NextContainerRef (the containerRef of the
/// NextContainer element — the target of validation rule R10).
/// </summary>
public sealed record RestrictionCriteria(
    Comparison? Comparison = null,
    IReadOnlyList<Comparison>? ComparisonList = null,
    string? NextContainerRef = null,
    RawXmlFragment? Raw = null)
{
    public bool Equals(RestrictionCriteria? other) =>
        other is not null
        && Equals(Comparison, other.Comparison)
        && Structural.ListEquals(ComparisonList, other.ComparisonList)
        && NextContainerRef == other.NextContainerRef
        && Equals(Raw, other.Raw);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Comparison);
        Structural.AddList(ref hash, ComparisonList);
        hash.Add(NextContainerRef);
        hash.Add(Raw);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A SequenceContainer's BaseContainer element: the container this one extends, with the
/// conditions under which the extension is instantiable.
/// </summary>
public sealed record BaseContainer(string ContainerRef, RestrictionCriteria? RestrictionCriteria = null);

/// <summary>
/// A SequenceContainer in a ContainerSet — the binary layout of a packet (or a reusable
/// piece of one). Abstract stays null when the attribute is absent (XSD default false,
/// applied by consumers). Unmodeled children (DefaultRateInStream, RateInStreamSet,
/// BinaryEncoding, LongDescription, AliasSet, AncillaryDataSet) and attributes
/// (idlePattern, shortDescription) are preserved.
/// </summary>
public sealed record SequenceContainer(
    string Name,
    IReadOnlyList<SequenceEntry> EntryList,
    BaseContainer? BaseContainer = null,
    bool? Abstract = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(SequenceContainer? other) =>
        other is not null
        && Name == other.Name
        && EntryList.SequenceEqual(other.EntryList)
        && Equals(BaseContainer, other.BaseContainer)
        && Abstract == other.Abstract
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        foreach (var entry in EntryList)
            hash.Add(entry);
        hash.Add(BaseContainer);
        hash.Add(Abstract);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}
