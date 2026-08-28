namespace Xtce.Workshop.Model;

/// <summary>
/// One ServiceSet entry (issue #115): name plus the referenced containers/messages
/// (ContainerRefSet/MessageRefSet ref attributes). Other children ride in Preserved; a
/// foreign set entry rides whole as RawXml.
/// </summary>
public sealed record Service(
    string Name,
    IReadOnlyList<string>? ContainerRefs = null,
    IReadOnlyList<string>? MessageRefs = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null,
    Description? Description = null,
    RawXmlFragment? RawXml = null)
{
    public bool Equals(Service? other) =>
        other is not null
        && Name == other.Name
        && Structural.ListEquals(ContainerRefs, other.ContainerRefs)
        && Structural.ListEquals(MessageRefs, other.MessageRefs)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes)
        && Equals(Description, other.Description)
        && Equals(RawXml, other.RawXml);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        Structural.AddList(ref hash, ContainerRefs);
        Structural.AddList(ref hash, MessageRefs);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        hash.Add(Description);
        hash.Add(RawXml);
        return hash.ToHashCode();
    }
}
