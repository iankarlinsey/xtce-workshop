namespace Xtce.Workshop.Model;

/// <summary>
/// A SpaceSystem element in an XTCE document — root or nested. `Name` is the one attribute
/// the XSD marks required (see reference/1.2/SpaceSystem.xsd, NameDescriptionType);
/// `Children` reflects that a SpaceSystem may recursively contain child SpaceSystems
/// (SpaceSystemType: &lt;element ref="xtce:SpaceSystem" minOccurs="0" maxOccurs="unbounded"/&gt;);
/// `TelemetryMetaData` is optional (minOccurs="0" in the XSD) and null on SpaceSystem nodes
/// that don't define any parameters. Extend further as later slices need more of the
/// document (CommandMetaData, Header, etc.).
/// </summary>
public sealed record SpaceSystem(
    string Name,
    IReadOnlyList<SpaceSystem> Children,
    TelemetryMetaData? TelemetryMetaData = null)
{
    // Record-generated equality compares Children by the collection instance/type, not
    // its contents — two structurally identical trees built from a List vs. an array (or
    // just two different List instances) would otherwise compare unequal. Override with
    // an explicit structural (element-by-element, order-sensitive) comparison instead.
    // TelemetryMetaData already has its own value-equality override, so a plain Equals()
    // comparison (which is null-safe) is sufficient for it.
    public bool Equals(SpaceSystem? other) =>
        other is not null
        && Name == other.Name
        && Children.SequenceEqual(other.Children)
        && Equals(TelemetryMetaData, other.TelemetryMetaData);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        foreach (var child in Children)
            hash.Add(child);
        hash.Add(TelemetryMetaData);
        return hash.ToHashCode();
    }
}
