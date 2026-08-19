namespace Xtce.Workshop.Model;

/// <summary>
/// A Message in a MessageSet: identifies a container by matching criteria. MatchCriteria
/// (required by the XSD, a whole expression language) rides in Preserved so round-trips
/// stay lossless without modeling it; ContainerRef is the containerRef attribute of the
/// ContainerRef child element — the target of validation rule R09.
/// </summary>
public sealed record Message(
    string Name,
    string ContainerRef,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(Message? other) =>
        other is not null
        && Name == other.Name
        && ContainerRef == other.ContainerRef
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(ContainerRef);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>
/// The MessageSet element of a TelemetryMetaData. A wrapper record (not a bare list)
/// because MessageSetType extends OptionalNameDescriptionType — the set itself may carry a
/// name attribute and description children, preserved here.
/// </summary>
public sealed record MessageSet(
    IReadOnlyList<Message> Messages,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(MessageSet? other) =>
        other is not null
        && Messages.SequenceEqual(other.Messages)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var message in Messages)
            hash.Add(message);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}
