namespace Xtce.Workshop.Model;

/// <summary>
/// Which XTCE ParameterTypeSet element a ParameterTypeDefinition represents. A closed-union
/// (discriminated-by-enum) shape was chosen over a record type hierarchy: this project already
/// discriminates XML element kinds by name when reading/writing (see XtceDocumentReader/Writer),
/// so a Kind field maps directly onto that existing decision logic, and it sidesteps
/// System.Text.Json polymorphic serialization at the API's JSON boundary — on .NET 8 STJ
/// requires the type discriminator to be the FIRST property of incoming JSON, which a browser
/// client round-tripping documents through object spreads cannot reliably guarantee. Kind-
/// specific fields are nullable and only meaningful for their corresponding Kind.
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
    Array,
    Aggregate,
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

/// <summary>Which calibrator element a modeled Calibrator represents.</summary>
public enum CalibratorKind
{
    Polynomial,
    Spline,
}

/// <summary>One PolynomialCalibrator Term. Values stay verbatim strings (doubles round-trip untouched).</summary>
public sealed record PolynomialTerm(
    string Coefficient,
    string Exponent,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(PolynomialTerm? other) =>
        other is not null
        && Coefficient == other.Coefficient
        && Exponent == other.Exponent
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Coefficient);
        hash.Add(Exponent);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>One SplineCalibrator SplinePoint. Values stay verbatim strings.</summary>
public sealed record SplinePointEntry(
    string Raw,
    string Calibrated,
    string? Order = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(SplinePointEntry? other) =>
        other is not null
        && Raw == other.Raw
        && Calibrated == other.Calibrated
        && Order == other.Order
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Raw);
        hash.Add(Calibrated);
        hash.Add(Order);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A modeled DefaultCalibrator: PolynomialCalibrator terms or SplineCalibrator points
/// (order/extrapolate stay null when absent — XSD defaults 1/false applied by consumers).
/// name/shortDescription ride in PreservedAttributes, AncillaryDataSet in Preserved. A
/// DefaultCalibrator wrapping MathOperationCalibrator (or anything unrecognizable) is
/// NOT modeled — the whole element stays a preserved fragment on the encoding.
/// </summary>
public sealed record Calibrator(
    CalibratorKind Kind,
    IReadOnlyList<PolynomialTerm>? Terms = null,
    IReadOnlyList<SplinePointEntry>? Points = null,
    long? SplineOrder = null,
    bool? Extrapolate = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(Calibrator? other) =>
        other is not null
        && Kind == other.Kind
        && Structural.ListEquals(Terms, other.Terms)
        && Structural.ListEquals(Points, other.Points)
        && SplineOrder == other.SplineOrder
        && Extrapolate == other.Extrapolate
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        Structural.AddList(ref hash, Terms);
        Structural.AddList(ref hash, Points);
        hash.Add(SplineOrder);
        hash.Add(Extrapolate);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>One alarm severity range (FloatRangeType): bounds stay verbatim strings.</summary>
public sealed record AlarmRange(
    string? MinInclusive = null,
    string? MinExclusive = null,
    string? MaxInclusive = null,
    string? MaxExclusive = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(AlarmRange? other) =>
        other is not null
        && MinInclusive == other.MinInclusive
        && MinExclusive == other.MinExclusive
        && MaxInclusive == other.MaxInclusive
        && MaxExclusive == other.MaxExclusive
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MinInclusive);
        hash.Add(MinExclusive);
        hash.Add(MaxInclusive);
        hash.Add(MaxExclusive);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A numeric DefaultAlarm (Integer/Float types, issue #105): the StaticAlarmRanges
/// severity ladder is modeled (rangeForm + the five ranges, XSD defaults never baked
/// in); ChangeAlarmRanges, AlarmMultiRanges, AlarmConditions, CustomAlarm, and
/// AncillaryDataSet ride in Preserved; minViolations is modeled, other alarm attributes
/// preserved. A StaticAlarmRanges that can't be modeled cleanly stays a preserved
/// fragment alongside.
/// </summary>
public sealed record NumericAlarm(
    long? MinViolations = null,
    string? RangeForm = null,
    AlarmRange? WatchRange = null,
    AlarmRange? WarningRange = null,
    AlarmRange? DistressRange = null,
    AlarmRange? CriticalRange = null,
    AlarmRange? SevereRange = null,
    bool HasStaticRanges = false,
    IReadOnlyList<RawAttribute>? StaticRangesPreservedAttributes = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(NumericAlarm? other) =>
        other is not null
        && MinViolations == other.MinViolations
        && RangeForm == other.RangeForm
        && Equals(WatchRange, other.WatchRange)
        && Equals(WarningRange, other.WarningRange)
        && Equals(DistressRange, other.DistressRange)
        && Equals(CriticalRange, other.CriticalRange)
        && Equals(SevereRange, other.SevereRange)
        && HasStaticRanges == other.HasStaticRanges
        && Structural.ListEquals(StaticRangesPreservedAttributes, other.StaticRangesPreservedAttributes)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MinViolations);
        hash.Add(RangeForm);
        hash.Add(WatchRange);
        hash.Add(WarningRange);
        hash.Add(DistressRange);
        hash.Add(CriticalRange);
        hash.Add(SevereRange);
        hash.Add(HasStaticRanges);
        Structural.AddList(ref hash, StaticRangesPreservedAttributes);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>The four DataEncoding element kinds of the XSD's BaseDataType choice.</summary>
public enum DataEncodingKind
{
    Integer,
    Float,
    String,
    Binary,
}

/// <summary>
/// The data-encoding element on a scalar parameter/argument type (Integer/Float/String/
/// BinaryDataEncoding). Attributes are modeled; child elements — calibrators, the
/// SizeInBits/Variable size shapes, transform algorithms, ErrorDetectCorrect — ride in
/// Preserved in original order and are re-emitted verbatim, so nested shapes are never
/// decomposed and re-synthesized. Absent attributes stay null (XSD defaults such as
/// sizeInBits=8/32 are applied by consumers at check time, never baked in). The time
/// types' Encoding wrapper is a different XSD shape and stays a preserved fragment on
/// the type itself.
/// </summary>
public sealed record DataEncoding(
    DataEncodingKind Kind,
    string? Encoding = null,
    long? SizeInBits = null,
    string? ChangeThreshold = null,
    string? BitOrder = null,
    string? ByteOrder = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null,
    Calibrator? DefaultCalibrator = null)
{
    public bool Equals(DataEncoding? other) =>
        other is not null
        && Kind == other.Kind
        && Encoding == other.Encoding
        && SizeInBits == other.SizeInBits
        && ChangeThreshold == other.ChangeThreshold
        && BitOrder == other.BitOrder
        && ByteOrder == other.ByteOrder
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes)
        && Equals(DefaultCalibrator, other.DefaultCalibrator);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Encoding);
        hash.Add(SizeInBits);
        hash.Add(ChangeThreshold);
        hash.Add(BitOrder);
        hash.Add(ByteOrder);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        hash.Add(DefaultCalibrator);
        return hash.ToHashCode();
    }
}

/// <summary>
/// One Unit element in a UnitSet: the unit text plus its optional attributes. Absent
/// attributes stay null (XSD defaults power=1, factor=1, form=calibrated applied by
/// consumers, never baked in); power/factor are kept verbatim as strings.
/// </summary>
public sealed record Unit(
    string Value,
    string? Description = null,
    string? Power = null,
    string? Factor = null,
    string? Form = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(Unit? other) =>
        other is not null
        && Value == other.Value
        && Description == other.Description
        && Power == other.Power
        && Factor == other.Factor
        && Form == other.Form
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Value);
        hash.Add(Description);
        hash.Add(Power);
        hash.Add(Factor);
        hash.Add(Form);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>
/// The time types' Encoding wrapper (the XSD's EncodingType): units/scale/offset
/// attributes around an inner data-encoding choice, which reuses <see cref="DataEncoding"/>.
/// Absent attributes stay null (XSD defaults units=seconds, scale=1, offset=0 applied by
/// consumers, never baked in); scale/offset are kept verbatim as strings so numeric
/// formatting round-trips untouched.
/// </summary>
public sealed record TimeEncoding(
    string? Units = null,
    string? Scale = null,
    string? Offset = null,
    DataEncoding? DataEncoding = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(TimeEncoding? other) =>
        other is not null
        && Units == other.Units
        && Scale == other.Scale
        && Offset == other.Offset
        && Equals(DataEncoding, other.DataEncoding)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Units);
        hash.Add(Scale);
        hash.Add(Offset);
        hash.Add(DataEncoding);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>
/// One entry in a ParameterTypeSet — all ten kinds are modeled (issues #21/#28/#31):
/// the eight scalars (Integer, Float, String, Boolean, Enumerated, Binary, RelativeTime,
/// AbsoluteTime) plus Array (ArrayTypeRef + Dimensions) and Aggregate (Members).
/// TelemetryMetaData.PreservedParameterTypes now only carries kinds a future schema
/// version might add.
/// Time-type children (Encoding, ReferenceTime) live in Preserved; validation rules R14/R01
/// inspect those fragments rather than requiring the encodings to be modeled.
///
/// Modeled attributes stay null when absent from the source — XSD defaults (signed=true,
/// sizeInBits=32, oneStringValue="True", zeroStringValue="False") are applied by consumers
/// (validators) at check time, never baked in on load, so an attribute the author omitted
/// stays omitted on save. The scalar kinds' data-encoding element is modeled as
/// <see cref="DataEncoding"/>; other unmodeled child elements (UnitSet, alarms,
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
    IReadOnlyList<RawAttribute>? PreservedAttributes = null,
    string? ArrayTypeRef = null,
    IReadOnlyList<Dimension>? Dimensions = null,
    IReadOnlyList<Member>? Members = null,
    DataEncoding? DataEncoding = null,
    TimeEncoding? TimeEncoding = null,
    IReadOnlyList<Unit>? UnitSet = null,
    IReadOnlyList<RawXmlFragment>? PreservedUnits = null,
    NumericAlarm? DefaultAlarm = null,
    Description? Description = null)
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
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes)
        && ArrayTypeRef == other.ArrayTypeRef
        && Structural.ListEquals(Dimensions, other.Dimensions)
        && Structural.ListEquals(Members, other.Members)
        && Equals(DataEncoding, other.DataEncoding)
        && Equals(TimeEncoding, other.TimeEncoding)
        && Structural.ListEquals(UnitSet, other.UnitSet)
        && Structural.ListEquals(PreservedUnits, other.PreservedUnits)
        && Equals(DefaultAlarm, other.DefaultAlarm)
        && Equals(Description, other.Description);

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
        hash.Add(ArrayTypeRef);
        Structural.AddList(ref hash, Dimensions);
        Structural.AddList(ref hash, Members);
        hash.Add(DataEncoding);
        hash.Add(TimeEncoding);
        Structural.AddList(ref hash, UnitSet);
        Structural.AddList(ref hash, PreservedUnits);
        hash.Add(DefaultAlarm);
        hash.Add(Description);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A named, typed telemetry parameter — a Parameter element in a ParameterSet. Unmodeled
/// child elements (ParameterProperties, LongDescription, AliasSet, AncillaryDataSet) and
/// attributes (shortDescription) are preserved, not dropped.
/// </summary>
/// <summary>
/// A Parameter's ParameterProperties element: dataSource/readOnly/persistence attributes
/// modeled (absent stays null — XSD defaults readOnly=false, persistence=true applied by
/// consumers, never baked in); children (SystemName, ValidityCondition,
/// PhysicalAddressSet, TimeAssociation) ride in Preserved.
/// </summary>
public sealed record ParameterProperties(
    string? DataSource = null,
    bool? ReadOnly = null,
    bool? Persistence = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(ParameterProperties? other) =>
        other is not null
        && DataSource == other.DataSource
        && ReadOnly == other.ReadOnly
        && Persistence == other.Persistence
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DataSource);
        hash.Add(ReadOnly);
        hash.Add(Persistence);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

public sealed record Parameter(
    string Name,
    string ParameterTypeRef,
    string? InitialValue = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null,
    ParameterProperties? Properties = null,
    Description? Description = null)
{
    public bool Equals(Parameter? other) =>
        other is not null
        && Name == other.Name
        && ParameterTypeRef == other.ParameterTypeRef
        && InitialValue == other.InitialValue
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes)
        && Equals(Properties, other.Properties)
        && Equals(Description, other.Description);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(ParameterTypeRef);
        hash.Add(InitialValue);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        hash.Add(Properties);
        hash.Add(Description);
        return hash.ToHashCode();
    }
}

/// <summary>
/// The TelemetryMetaData element of a SpaceSystem: its parameter type definitions and the
/// parameters that reference them. Both lists default to empty rather than null so callers
/// don't need null-conditional access, but the TelemetryMetaData element itself is nullable
/// on SpaceSystem since the XSD marks it minOccurs="0".
///
/// PreservedParameterTypes holds ParameterTypeSet entries of kinds this model doesn't
/// recognize (all ten current kinds are modeled — this is future-schema insurance);
/// PreservedParameters holds unmodeled ParameterSet entries (ParameterRef). Both sets are XSD choice-unbounded, so re-emitting
/// preserved entries after the modeled ones is order-valid. ContainerSet and MessageSet are modeled; Preserved holds the remaining unmodeled
/// TelemetryMetaData children (StreamSet, AlgorithmSet), re-emitted in XSD sequence order.
/// </summary>
public sealed record TelemetryMetaData(
    IReadOnlyList<ParameterTypeDefinition> ParameterTypeSet,
    IReadOnlyList<Parameter> ParameterSet,
    IReadOnlyList<RawXmlFragment>? PreservedParameterTypes = null,
    IReadOnlyList<RawXmlFragment>? PreservedParameters = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<SequenceContainer>? ContainerSet = null,
    MessageSet? MessageSet = null,
    IReadOnlyList<RawXmlFragment>? PreservedContainerEntries = null,
    IReadOnlyList<Algorithm>? AlgorithmSet = null,
    IReadOnlyList<RawXmlFragment>? PreservedAlgorithms = null,
    IReadOnlyList<StreamDefinition>? StreamSet = null)
{
    public bool Equals(TelemetryMetaData? other) =>
        other is not null
        && ParameterTypeSet.SequenceEqual(other.ParameterTypeSet)
        && ParameterSet.SequenceEqual(other.ParameterSet)
        && Structural.ListEquals(PreservedParameterTypes, other.PreservedParameterTypes)
        && Structural.ListEquals(PreservedParameters, other.PreservedParameters)
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(ContainerSet, other.ContainerSet)
        && Equals(MessageSet, other.MessageSet)
        && Structural.ListEquals(PreservedContainerEntries, other.PreservedContainerEntries)
        && Structural.ListEquals(AlgorithmSet, other.AlgorithmSet)
        && Structural.ListEquals(PreservedAlgorithms, other.PreservedAlgorithms)
        && Structural.ListEquals(StreamSet, other.StreamSet);

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
        hash.Add(MessageSet);
        Structural.AddList(ref hash, PreservedContainerEntries);
        Structural.AddList(ref hash, AlgorithmSet);
        Structural.AddList(ref hash, PreservedAlgorithms);
        Structural.AddList(ref hash, StreamSet);
        return hash.ToHashCode();
    }
}
