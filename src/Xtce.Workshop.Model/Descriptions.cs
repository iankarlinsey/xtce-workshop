namespace Xtce.Workshop.Model;

/// <summary>One Alias in an AliasSet: the namespace it aliases within and the alias itself.</summary>
public sealed record AliasEntry(
    string NameSpace,
    string Alias,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(AliasEntry? other) =>
        other is not null
        && NameSpace == other.NameSpace
        && Alias == other.Alias
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(NameSpace);
        hash.Add(Alias);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>One AncillaryData row: name plus its text value and optional mimeType/href.</summary>
public sealed record AncillaryDataEntry(
    string Name,
    string Value,
    string? MimeType = null,
    string? Href = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(AncillaryDataEntry? other) =>
        other is not null
        && Name == other.Name
        && Value == other.Value
        && MimeType == other.MimeType
        && Href == other.Href
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Value);
        hash.Add(MimeType);
        hash.Add(Href);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>
/// The NameDescription trio every named XTCE construct shares (issue #113):
/// LongDescription text, Alias rows, and AncillaryData rows. Null lists mean the set
/// element was absent; empty lists mean an empty element — the same fidelity convention
/// as UnitSet. Foreign content inside a set (or an AncillaryData whose value isn't plain
/// text) rides in the matching preserved list and re-emits inside the set element; an
/// unmodelable LongDescription stays a preserved fragment on the construct itself.
/// </summary>
public sealed record Description(
    string? LongDescription = null,
    IReadOnlyList<AliasEntry>? Aliases = null,
    IReadOnlyList<RawXmlFragment>? PreservedAliases = null,
    IReadOnlyList<AncillaryDataEntry>? AncillaryData = null,
    IReadOnlyList<RawXmlFragment>? PreservedAncillaryData = null)
{
    public bool Equals(Description? other) =>
        other is not null
        && LongDescription == other.LongDescription
        && Structural.ListEquals(Aliases, other.Aliases)
        && Structural.ListEquals(PreservedAliases, other.PreservedAliases)
        && Structural.ListEquals(AncillaryData, other.AncillaryData)
        && Structural.ListEquals(PreservedAncillaryData, other.PreservedAncillaryData);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(LongDescription);
        Structural.AddList(ref hash, Aliases);
        Structural.AddList(ref hash, PreservedAliases);
        Structural.AddList(ref hash, AncillaryData);
        Structural.AddList(ref hash, PreservedAncillaryData);
        return hash.ToHashCode();
    }

    /// <summary>Whether anything at all is modeled here (used to skip emitting).</summary>
    public bool IsEmpty =>
        LongDescription is null && Aliases is null && PreservedAliases is null
        && AncillaryData is null && PreservedAncillaryData is null;
}
