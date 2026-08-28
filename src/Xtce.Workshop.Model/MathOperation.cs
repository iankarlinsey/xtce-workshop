namespace Xtce.Workshop.Model;

/// <summary>One postfix term kind in a MathOperation (issue #125).</summary>
public enum MathOperandKind
{
    Value,
    ThisParameter,
    Operator,
    ParameterInstanceRef,
}

/// <summary>
/// One term of a postfix (RPN) MathOperation: a Value or Operator carries its text
/// verbatim; a ParameterInstanceRefOperand carries the instance ref; ThisParameterOperand
/// carries nothing (the XSD fixes its content to empty).
/// </summary>
public sealed record MathOperationTerm(
    MathOperandKind Kind,
    string? Text = null,
    ParameterInstanceRef? InstanceRef = null);

/// <summary>
/// A MathAlgorithm's MathOperation body (TriggeredMathOperationType, issue #125): the
/// postfix term list, the required outputParameterRef, and the TriggerSet kept verbatim
/// (the trigger choice is its own sub-language).
/// </summary>
public sealed record TriggeredMathOperation(
    IReadOnlyList<MathOperationTerm> Terms,
    string OutputParameterRef,
    RawXmlFragment? TriggerSet = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(TriggeredMathOperation? other) =>
        other is not null
        && Structural.ListEquals(Terms, other.Terms)
        && OutputParameterRef == other.OutputParameterRef
        && Equals(TriggerSet, other.TriggerSet)
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        Structural.AddList(ref hash, Terms);
        hash.Add(OutputParameterRef);
        hash.Add(TriggerSet);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}
