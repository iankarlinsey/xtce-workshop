using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>Counts for one SpaceSystem node (Local) and its whole subtree (Deep).</summary>
public sealed record SpaceSystemMetrics(
    string SystemPath,
    MetricCounts Local,
    MetricCounts Deep);

public sealed record MetricCounts(
    int ChildSystems,
    int Parameters,
    int ParameterTypes,
    IReadOnlyDictionary<string, int> ParameterTypesByKind,
    int Containers,
    int Messages,
    int MetaCommands,
    int PreservedFragments)
{
    public static MetricCounts operator +(MetricCounts a, MetricCounts b)
    {
        var kinds = new Dictionary<string, int>(a.ParameterTypesByKind);
        foreach (var (kind, count) in b.ParameterTypesByKind)
        {
            kinds[kind] = kinds.GetValueOrDefault(kind) + count;
        }
        return new MetricCounts(
            a.ChildSystems + b.ChildSystems,
            a.Parameters + b.Parameters,
            a.ParameterTypes + b.ParameterTypes,
            kinds,
            a.Containers + b.Containers,
            a.Messages + b.Messages,
            a.MetaCommands + b.MetaCommands,
            a.PreservedFragments + b.PreservedFragments);
    }
}

public sealed record DocumentMetrics(
    MetricCounts Totals,
    IReadOnlyList<SpaceSystemMetrics> Systems);

/// <summary>
/// Per-SpaceSystem and document-total counts. PreservedFragments counts the raw-XML fragments the
/// model carries without modeling (comments excluded) — a transparency measure of how much
/// of the document rides opaque.
/// </summary>
public static class XtceDocumentMetrics
{
    public static DocumentMetrics Compute(SpaceSystem root)
    {
        var systems = new List<SpaceSystemMetrics>();
        var totals = Walk(SpaceSystemContext.Build(root), systems);
        return new DocumentMetrics(totals, systems);
    }

    private static MetricCounts Walk(SpaceSystemContext context, List<SpaceSystemMetrics> systems)
    {
        var local = CountLocal(context);
        var index = systems.Count;
        systems.Add(new SpaceSystemMetrics(context.Path, local, local)); // Deep placeholder, patched below

        var deep = local;
        foreach (var child in context.ChildrenByName.Values)
        {
            deep += Walk(child, systems);
        }

        systems[index] = systems[index] with { Deep = deep };
        return deep;
    }

    private static MetricCounts CountLocal(SpaceSystemContext context)
    {
        var node = context.Node;
        var telemetry = node.TelemetryMetaData;
        var kinds = (telemetry?.ParameterTypeSet ?? [])
            .GroupBy(t => t.Kind.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        var preservedFragments = 0;
        foreach (var (fragment, _) in FragmentEnumerator.EnumerateNode(context))
        {
            if (fragment.ElementName != CommentAnchor.ElementName)
            {
                preservedFragments++;
            }
        }

        return new MetricCounts(
            ChildSystems: node.Children.Count,
            Parameters: telemetry?.ParameterSet.Count ?? 0,
            ParameterTypes: telemetry?.ParameterTypeSet.Count ?? 0,
            ParameterTypesByKind: kinds,
            Containers: telemetry?.ContainerSet?.Count ?? 0,
            Messages: telemetry?.MessageSet?.Messages.Count ?? 0,
            MetaCommands: node.CommandMetaData?.MetaCommands.Count ?? 0,
            PreservedFragments: preservedFragments);
    }
}
