namespace Xtce.Workshop.Model;

/// <summary>
/// A SpaceSystem element in an XTCE document — root or nested. Deliberately minimal
/// today: `Name` (the one attribute the XSD marks required — see
/// reference/1.2/SpaceSystem.xsd, NameDescriptionType) and `Children`, since a
/// SpaceSystem may recursively contain child SpaceSystems
/// (SpaceSystemType: &lt;element ref="xtce:SpaceSystem" minOccurs="0" maxOccurs="unbounded"/&gt;).
/// Extend as later slices need more of the document (TelemetryMetaData, etc.).
/// </summary>
public sealed record SpaceSystem(string Name, IReadOnlyList<SpaceSystem> Children);
