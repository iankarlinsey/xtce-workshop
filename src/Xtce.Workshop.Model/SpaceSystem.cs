namespace Xtce.Workshop.Model;

/// <summary>
/// The root SpaceSystem of an XTCE document. Deliberately minimal today — only the
/// one attribute the XSD marks required (see reference/1.2/SpaceSystem.xsd: `name`
/// is the sole use="required" attribute across the SpaceSystemType/NameDescriptionType
/// chain). Extend as later slices need more of the document.
/// </summary>
public sealed record SpaceSystem(string Name);
