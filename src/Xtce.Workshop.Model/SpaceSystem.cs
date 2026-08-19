namespace Xtce.Workshop.Model;

/// <summary>
/// A SpaceSystem element in an XTCE document — root or nested. Deliberately minimal
/// today: `Name` (the one attribute the XSD marks required — see
/// reference/1.2/SpaceSystem.xsd, NameDescriptionType) and `Children`, since a
/// SpaceSystem may recursively contain child SpaceSystems
/// (SpaceSystemType: &lt;element ref="xtce:SpaceSystem" minOccurs="0" maxOccurs="unbounded"/&gt;).
/// Extend as later slices need more of the document (TelemetryMetaData, etc.).
/// </summary>
public sealed record SpaceSystem(string Name, IReadOnlyList<SpaceSystem> Children)
{
    // Record-generated equality compares Children by the collection instance/type, not
    // its contents — two structurally identical trees built from a List vs. an array (or
    // just two different List instances) would otherwise compare unequal. Override with
    // an explicit structural (element-by-element, order-sensitive) comparison instead.
    public bool Equals(SpaceSystem? other) =>
        other is not null && Name == other.Name && Children.SequenceEqual(other.Children);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        foreach (var child in Children)
            hash.Add(child);
        return hash.ToHashCode();
    }
}
