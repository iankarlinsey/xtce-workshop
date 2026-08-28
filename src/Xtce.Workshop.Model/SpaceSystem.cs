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
/// declarations) captured verbatim on load and written back on save — see RawXml.cs. Extend the modeled surface as later slices need more
/// of the document; preservation keeps the gap lossless in the meantime.
/// </summary>
/// <summary>
/// The document Header (issue #110): version/date/classification/validationStatus
/// attributes modeled verbatim; AuthorSet/NoteSet/HistorySet children ride in Preserved
/// (their entries are display-only text rows a later slice can model).
/// </summary>
public sealed record Header(
    string? Version = null,
    string? Date = null,
    string? Classification = null,
    string? ClassificationInstructions = null,
    string? ValidationStatus = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(Header? other) =>
        other is not null
        && Version == other.Version
        && Date == other.Date
        && Classification == other.Classification
        && ClassificationInstructions == other.ClassificationInstructions
        && ValidationStatus == other.ValidationStatus
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Version);
        hash.Add(Date);
        hash.Add(Classification);
        hash.Add(ClassificationInstructions);
        hash.Add(ValidationStatus);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

public sealed record SpaceSystem(
    string Name,
    IReadOnlyList<SpaceSystem> Children,
    TelemetryMetaData? TelemetryMetaData = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null,
    CommandMetaData? CommandMetaData = null,
    Header? Header = null)
{
    public bool Equals(SpaceSystem? other) =>
        other is not null
        && Name == other.Name
        && Children.SequenceEqual(other.Children)
        && Equals(TelemetryMetaData, other.TelemetryMetaData)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes)
        && Equals(CommandMetaData, other.CommandMetaData)
        && Equals(Header, other.Header);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        foreach (var child in Children)
            hash.Add(child);
        hash.Add(TelemetryMetaData);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        hash.Add(CommandMetaData);
        hash.Add(Header);
        return hash.ToHashCode();
    }
}
