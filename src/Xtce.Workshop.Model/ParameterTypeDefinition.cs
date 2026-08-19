namespace Xtce.Workshop.Model;

/// <summary>
/// Which XTCE ParameterTypeSet element a ParameterTypeDefinition represents. A closed-union
/// (discriminated-by-enum) shape was chosen over a record type hierarchy: this project already
/// discriminates XML element kinds by name when reading/writing (see XtceDocumentReader/Writer),
/// so a Kind field maps directly onto that existing decision logic, and it sidesteps
/// System.Text.Json polymorphic serialization at the API's JSON boundary — on .NET 8 STJ
/// requires the type discriminator to be the FIRST property of incoming JSON, which a browser
/// client round-tripping documents through object spreads cannot reliably guarantee. Kind-
/// specific fields are nullable and only meaningful for their corresponding Kind — see
/// summary.md's Architecture Decisions for the tradeoff discussion and the revisit trigger.
/// </summary>
public enum ParameterTypeKind
{
    Integer,
    Float,
    String,
    Boolean,
    Enumerated,
    Binary,
    RelativeTime,
    AbsoluteTime,
}

/// <summary>
/// One Value/Label pair in an EnumeratedParameterType's EnumerationList. MaxValue (when set)
/// makes the label cover the inclusive range [Value, MaxValue]; ShortDescription is the
/// XSD's optional per-label description. Both are modeled outright (they're plain
/// attributes) rather than raw-preserved.
/// </summary>
public sealed record EnumerationEntry(
    long Value,
    string Label,
    long? MaxValue = null,
    string? ShortDescription = null);

/// <summary>
/// One entry in a ParameterTypeSet — any of the eight modeled scalar kinds (Integer, Float,
/// String, Boolean, Enumerated, Binary, RelativeTime, AbsoluteTime — issues #21/#28). Only
/// Array and Aggregate parameter types remain unmodeled, preserved as raw fragments on
/// TelemetryMetaData.PreservedParameterTypes (issue #23), not lossily represented here.
/// Time-type children (Encoding, ReferenceTime) live in Preserved; validation rules R14/R01
/// inspect those fragments rather than requiring the encodings to be modeled.
///
/// Modeled attributes stay null when absent from the source — XSD defaults (signed=true,
/// sizeInBits=32, oneStringValue="True", zeroStringValue="False") are applied by consumers
/// (validators) at check time, never baked in on load, so an attribute the author omitted
/// stays omitted on save. Unmodeled child elements (UnitSet, data encodings, alarms,
/// ValidRange, ToString, aliases...) live in Preserved; unmodeled attributes (baseType,
/// shortDescription, restrictionPattern, characterWidth...) in PreservedAttributes.
/// </summary>
public sealed record ParameterTypeDefinition(
    string Name,
    ParameterTypeKind Kind,
    string? InitialValue = null,
    bool? Signed = null,
    long? SizeInBits = null,
    string? OneStringValue = null,
    string? ZeroStringValue = null,
    IReadOnlyList<EnumerationEntry>? Enumerations = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(ParameterTypeDefinition? other) =>
        other is not null
        && Name == other.Name
        && Kind == other.Kind
        && InitialValue == other.InitialValue
        && Signed == other.Signed
        && SizeInBits == other.SizeInBits
        && OneStringValue == other.OneStringValue
        && ZeroStringValue == other.ZeroStringValue
        && Structural.ListEquals(Enumerations, other.Enumerations)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Kind);
        hash.Add(InitialValue);
        hash.Add(Signed);
        hash.Add(SizeInBits);
        hash.Add(OneStringValue);
        hash.Add(ZeroStringValue);
        Structural.AddList(ref hash, Enumerations);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A named, typed telemetry parameter — a Parameter element in a ParameterSet. Unmodeled
/// child elements (ParameterProperties, LongDescription, AliasSet, AncillaryDataSet) and
/// attributes (shortDescription) are preserved, not dropped.
/// </summary>
public sealed record Parameter(
    string Name,
    string ParameterTypeRef,
    string? InitialValue = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(Parameter? other) =>
        other is not null
        && Name == other.Name
        && ParameterTypeRef == other.ParameterTypeRef
        && InitialValue == other.InitialValue
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(ParameterTypeRef);
        hash.Add(InitialValue);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>
/// The TelemetryMetaData element of a SpaceSystem: its parameter type definitions and the
/// parameters that reference them. Both lists default to empty rather than null so callers
/// don't need null-conditional access, but the TelemetryMetaData element itself is nullable
/// on SpaceSystem since the XSD marks it minOccurs="0".
///
/// PreservedParameterTypes holds unmodeled ParameterTypeSet entries (Binary/RelativeTime/
/// AbsoluteTime/Array/Aggregate parameter types); PreservedParameters holds unmodeled
/// ParameterSet entries (ParameterRef). Both sets are XSD choice-unbounded, so re-emitting
/// preserved entries after the modeled ones is order-valid. ContainerSet is modeled (issue
/// #24); Preserved holds the remaining unmodeled TelemetryMetaData children (MessageSet,
/// StreamSet, AlgorithmSet), re-emitted in XSD sequence order.
/// </summary>
public sealed record TelemetryMetaData(
    IReadOnlyList<ParameterTypeDefinition> ParameterTypeSet,
    IReadOnlyList<Parameter> ParameterSet,
    IReadOnlyList<RawXmlFragment>? PreservedParameterTypes = null,
    IReadOnlyList<RawXmlFragment>? PreservedParameters = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<SequenceContainer>? ContainerSet = null)
{
    public bool Equals(TelemetryMetaData? other) =>
        other is not null
        && ParameterTypeSet.SequenceEqual(other.ParameterTypeSet)
        && ParameterSet.SequenceEqual(other.ParameterSet)
        && Structural.ListEquals(PreservedParameterTypes, other.PreservedParameterTypes)
        && Structural.ListEquals(PreservedParameters, other.PreservedParameters)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(ContainerSet, other.ContainerSet);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var type in ParameterTypeSet)
        {
            hash.Add(type);
        }
        foreach (var parameter in ParameterSet)
        {
            hash.Add(parameter);
        }
        Structural.AddList(ref hash, PreservedParameterTypes);
        Structural.AddList(ref hash, PreservedParameters);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, ContainerSet);
        return hash.ToHashCode();
    }
}
