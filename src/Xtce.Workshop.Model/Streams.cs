namespace Xtce.Workshop.Model;

/// <summary>Which StreamSet element a Stream represents.</summary>
public enum StreamKind
{
    FixedFrame,
    VariableFrame,
    Custom,
}

/// <summary>
/// One StreamSet entry (issue #114), modeled shallowly: name, kind, the ContainerRef
/// target when the frame content is a container (a ServiceRef target stays preserved),
/// and the statically interesting attributes verbatim. SyncStrategy, encodings, and
/// every other child ride in Preserved; a foreign set entry rides whole as RawXml.
/// </summary>
public sealed record StreamDefinition(
    string Name,
    StreamKind Kind,
    string? ContainerRef = null,
    string? FrameLengthInBits = null,
    string? BitRateInBps = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null,
    Description? Description = null,
    RawXmlFragment? RawXml = null)
{
    public bool Equals(StreamDefinition? other) =>
        other is not null
        && Name == other.Name
        && Kind == other.Kind
        && ContainerRef == other.ContainerRef
        && FrameLengthInBits == other.FrameLengthInBits
        && BitRateInBps == other.BitRateInBps
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes)
        && Equals(Description, other.Description)
        && Equals(RawXml, other.RawXml);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Kind);
        hash.Add(ContainerRef);
        hash.Add(FrameLengthInBits);
        hash.Add(BitRateInBps);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        hash.Add(Description);
        hash.Add(RawXml);
        return hash.ToHashCode();
    }
}
