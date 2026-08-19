using Xtce.Workshop.Model;

namespace Xtce.Workshop.Api;

/// <summary>
/// A generic tree node for the sidebar UI — deliberately decoupled from the domain
/// model (SpaceSystem, and later TelemetryMetaData, Parameter, Container, etc.). The
/// frontend tree component renders {label, nodeType, children} without knowing what an
/// XTCE construct is; only the mapper below needs to change as the domain model grows
/// to cover more of the spec. NodeType exists as a hook for later features (icons,
/// filtering, the reference-sheet panel) — it doesn't carry meaning beyond a tag yet.
/// </summary>
public sealed record TreeNode(string Label, string NodeType, IReadOnlyList<TreeNode> Children)
{
    // Same record-equality gap as SpaceSystem (see its comment) — Children needs an
    // explicit structural comparison, the generated one compares by collection instance.
    public bool Equals(TreeNode? other) =>
        other is not null
        && Label == other.Label
        && NodeType == other.NodeType
        && Children.SequenceEqual(other.Children);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Label);
        hash.Add(NodeType);
        foreach (var child in Children)
            hash.Add(child);
        return hash.ToHashCode();
    }

    public static TreeNode FromSpaceSystem(SpaceSystem spaceSystem) =>
        new(
            Label: spaceSystem.Name,
            NodeType: "SpaceSystem",
            Children: spaceSystem.Children.Select(FromSpaceSystem).ToList());
}
