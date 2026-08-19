namespace Xtce.Workshop.Model;

/// <summary>
/// Which XTCE ParameterTypeSet element a ParameterTypeDefinition represents. A closed-union
/// (discriminated-by-enum) shape was chosen over a record type hierarchy: this project already
/// discriminates XML element kinds by name when reading/writing (see XtceDocumentReader/Writer),
/// so a Kind field maps directly onto that existing decision logic, and it sidesteps configuring
/// System.Text.Json polymorphic serialization for the API's JSON boundary. Kind-specific fields
/// are nullable and only meaningful for their corresponding Kind — see summary.md's Architecture
/// Decisions for the tradeoff discussion.
/// </summary>
public enum ParameterTypeKind
{
    Integer,
    Float,
    String,
    Boolean,
    Enumerated,
}

/// <summary>One Value/Label pair in an EnumeratedParameterType's EnumerationList.</summary>
public sealed record EnumerationEntry(long Value, string Label);

/// <summary>
/// One entry in a ParameterTypeSet — an IntegerParameterType, FloatParameterType,
/// StringParameterType, BooleanParameterType, or EnumeratedParameterType. Only the primitive
/// scalar kinds are modeled; Binary/RelativeTime/AbsoluteTime/Array/Aggregate parameter types
/// are out of scope for this slice (see issue #21) and are skipped, not lossily represented.
/// </summary>
public sealed record ParameterTypeDefinition(
    string Name,
    ParameterTypeKind Kind,
    string? InitialValue = null,
    bool? Signed = null,
    long? SizeInBits = null,
    string? OneStringValue = null,
    string? ZeroStringValue = null,
    IReadOnlyList<EnumerationEntry>? Enumerations = null)
{
    // Enumerations is the only collection-typed property here — record-generated equality
    // would compare it by instance/type rather than contents (see SpaceSystem.cs for the
    // same gotcha), so it needs an explicit structural comparison.
    public bool Equals(ParameterTypeDefinition? other) =>
        other is not null
        && Name == other.Name
        && Kind == other.Kind
        && InitialValue == other.InitialValue
        && Signed == other.Signed
        && SizeInBits == other.SizeInBits
        && OneStringValue == other.OneStringValue
        && ZeroStringValue == other.ZeroStringValue
        && EnumerationsEqual(other.Enumerations);

    private bool EnumerationsEqual(IReadOnlyList<EnumerationEntry>? other)
    {
        if (Enumerations is null || other is null)
        {
            return Enumerations is null && other is null;
        }

        return Enumerations.SequenceEqual(other);
    }

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
        if (Enumerations is not null)
        {
            foreach (var entry in Enumerations)
            {
                hash.Add(entry);
            }
        }
        return hash.ToHashCode();
    }
}

/// <summary>A named, typed telemetry parameter — a Parameter element in a ParameterSet.</summary>
public sealed record Parameter(string Name, string ParameterTypeRef, string? InitialValue = null);

/// <summary>
/// The TelemetryMetaData element of a SpaceSystem: its parameter type definitions and the
/// parameters that reference them. Both lists default to empty rather than null so callers
/// don't need null-conditional access, but the TelemetryMetaData element itself is nullable
/// on SpaceSystem since the XSD marks it minOccurs="0" (most SpaceSystem nodes, e.g. a Bus or
/// Payload subsystem, may have none).
/// </summary>
public sealed record TelemetryMetaData(
    IReadOnlyList<ParameterTypeDefinition> ParameterTypeSet,
    IReadOnlyList<Parameter> ParameterSet)
{
    public bool Equals(TelemetryMetaData? other) =>
        other is not null
        && ParameterTypeSet.SequenceEqual(other.ParameterTypeSet)
        && ParameterSet.SequenceEqual(other.ParameterSet);

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
        return hash.ToHashCode();
    }
}
