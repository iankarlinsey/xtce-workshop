namespace Xtce.Workshop.Model;

/// <summary>What a <see cref="BooleanExpressionNode"/> represents.</summary>
public enum BooleanNodeKind
{
    Condition,
    And,
    Or,
}

/// <summary>
/// A ParameterInstanceRef inside a boolean-expression Condition. Instance and
/// UseCalibratedValue stay null when absent (XSD defaults 0/true applied by consumers,
/// never baked in).
/// </summary>
public sealed record ParameterInstanceRef(
    string ParameterRef,
    long? Instance = null,
    bool? UseCalibratedValue = null,
    IReadOnlyList<RawAttribute>? PreservedAttributes = null)
{
    public bool Equals(ParameterInstanceRef? other) =>
        other is not null
        && ParameterRef == other.ParameterRef
        && Instance == other.Instance
        && UseCalibratedValue == other.UseCalibratedValue
        && Structural.ListEquals(PreservedAttributes, other.PreservedAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ParameterRef);
        hash.Add(Instance);
        hash.Add(UseCalibratedValue);
        Structural.AddList(ref hash, PreservedAttributes);
        return hash.ToHashCode();
    }
}

/// <summary>
/// One node of a modeled BooleanExpression tree (issue #124). A Condition leaf carries
/// Left (the LHS instance ref), Operator (the ComparisonOperator text verbatim), and
/// exactly one of Value (RHS literal, verbatim) or Right (RHS instance ref). And/Or
/// nodes carry two-or-more Children (Condition or the opposite junction — the XSD
/// forbids nesting a junction directly inside itself). Any shape beyond this — comments,
/// argument-instance refs, foreign children — keeps the whole BooleanExpression as a
/// preserved fragment on the owning MatchCriteria instead.
/// </summary>
public sealed record BooleanExpressionNode(
    BooleanNodeKind Kind,
    ParameterInstanceRef? Left = null,
    string? Operator = null,
    string? Value = null,
    ParameterInstanceRef? Right = null,
    IReadOnlyList<BooleanExpressionNode>? Children = null)
{
    public bool Equals(BooleanExpressionNode? other) =>
        other is not null
        && Kind == other.Kind
        && Equals(Left, other.Left)
        && Operator == other.Operator
        && Value == other.Value
        && Equals(Right, other.Right)
        && Structural.ListEquals(Children, other.Children);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Left);
        hash.Add(Operator);
        hash.Add(Value);
        hash.Add(Right);
        Structural.AddList(ref hash, Children);
        return hash.ToHashCode();
    }

    /// <summary>Every Condition leaf in this subtree, in document order.</summary>
    public IEnumerable<BooleanExpressionNode> Leaves()
    {
        if (Kind == BooleanNodeKind.Condition)
        {
            yield return this;
            yield break;
        }
        foreach (var child in Children ?? [])
        {
            foreach (var leaf in child.Leaves())
            {
                yield return leaf;
            }
        }
    }
}
