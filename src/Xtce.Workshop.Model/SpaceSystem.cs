namespace Xtce.Workshop.Model;

/// <summary>
/// A SpaceSystem element in an XTCE document — root or nested. `Name` is the one attribute
/// the XSD marks required (see reference/1.2/SpaceSystem.xsd, NameDescriptionType);
/// `Children` reflects that a SpaceSystem may recursively contain child SpaceSystems
/// (SpaceSystemType: &lt;element ref="xtce:SpaceSystem" minOccurs="0" maxOccurs="unbounded"/&gt;);
/// `TelemetryMetaData` is optional (minOccurs="0" in the XSD) and null on SpaceSystem nodes
/// that don't define any parameters.
///
/// `Preserved` holds unmodeled child elements (LongDescription, AliasSet, AncillaryDataSet,
/// Header, CommandMetaData, ServiceSet) and `PreservedAttributes` unmodeled attributes
/// (shortDescription, operationalStatus, xml:base, xsi:schemaLocation, namespace
/// declarations) captured verbatim on load and written back on save — see RawXml.cs and
/// issue #23 (lossless round-trip). Extend the modeled surface as later slices need more
/// of the document; preservation keeps the gap lossless in the meantime.
/// </summary>
public sealed record SpaceSystem(
    string Name,
    IReadOnlyList<SpaceSystem> Children,
    TelemetryMetaData? TelemetryMetaData = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(SpaceSystem? other) =>
        other is not null
        && Name == other.Name
        && Children.SequenceEqual(other.Children)
        && Equals(TelemetryMetaData, other.TelemetryMetaData)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        foreach (var child in Children)
            hash.Add(child);
        hash.Add(TelemetryMetaData);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}
