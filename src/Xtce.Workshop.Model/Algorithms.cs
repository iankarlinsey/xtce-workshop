namespace Xtce.Workshop.Model;

/// <summary>Which AlgorithmSet element an Algorithm represents.</summary>
public enum AlgorithmKind
{
    /// <summary>CustomAlgorithm (InputOutputTriggerAlgorithmType).</summary>
    Custom,

    /// <summary>MathAlgorithm (MathAlgorithmType) — its MathOperation rides in Preserved.</summary>
    Math,
}

/// <summary>
/// One InputSet/OutputSet entry: the parameterRef plus the algorithm-local name
/// (inputName/outputName). Other attributes (instance, useCalibratedValue) ride in
/// PreservedAttributes.
/// </summary>
public sealed record AlgorithmParameterRef(
    string ParameterRef,
    string? Name = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(AlgorithmParameterRef? other) =>
        other is not null
        && ParameterRef == other.ParameterRef
        && Name == other.Name
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ParameterRef);
        hash.Add(Name);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>
/// One AlgorithmSet entry (issue #103): CustomAlgorithm's inheritance stack modeled as
/// one flat record — AlgorithmText (text + language, XSD default "pseudo" never baked
/// in), InputSet/OutputSet parameter refs, and the thread/triggerContainer/priority
/// attributes. MathAlgorithm shares the record with its MathOperation preserved.
/// PreservedInputs/PreservedOutputs re-emit INSIDE their sets (Constants, foreign
/// content); Preserved carries everything else (ExternalAlgorithmSet, TriggerSet,
/// MathOperation, description children). An AlgorithmText with unexpected attributes is
/// not modeled — it stays a preserved fragment so nothing is dropped.
/// </summary>
public sealed record Algorithm(
    string Name,
    AlgorithmKind Kind,
    string? AlgorithmText = null,
    string? Language = null,
    IReadOnlyList<AlgorithmParameterRef>? Inputs = null,
    IReadOnlyList<RawXmlFragment>? PreservedInputs = null,
    IReadOnlyList<AlgorithmParameterRef>? Outputs = null,
    IReadOnlyList<RawXmlFragment>? PreservedOutputs = null,
    bool? Thread = null,
    string? TriggerContainer = null,
    long? Priority = null,
    IReadOnlyList<RawXmlFragment>? Preserved = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null,
    Description? Description = null)
{
    public bool Equals(Algorithm? other) =>
        other is not null
        && Name == other.Name
        && Kind == other.Kind
        && AlgorithmText == other.AlgorithmText
        && Language == other.Language
        && Structural.ListEquals(Inputs, other.Inputs)
        && Structural.ListEquals(PreservedInputs, other.PreservedInputs)
        && Structural.ListEquals(Outputs, other.Outputs)
        && Structural.ListEquals(PreservedOutputs, other.PreservedOutputs)
        && Thread == other.Thread
        && TriggerContainer == other.TriggerContainer
        && Priority == other.Priority
        && Structural.ListEquals(Preserved, other.Preserved)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes)
        && Equals(Description, other.Description);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Kind);
        hash.Add(AlgorithmText);
        hash.Add(Language);
        Structural.AddList(ref hash, Inputs);
        Structural.AddList(ref hash, PreservedInputs);
        Structural.AddList(ref hash, Outputs);
        Structural.AddList(ref hash, PreservedOutputs);
        hash.Add(Thread);
        hash.Add(TriggerContainer);
        hash.Add(Priority);
        Structural.AddList(ref hash, Preserved);
        Structural.AddList(ref hash, PreservedAttributes);
        hash.Add(Description);
        return hash.ToHashCode();
    }
}
