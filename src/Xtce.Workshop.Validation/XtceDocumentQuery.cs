using System.Text.RegularExpressions;
using System.Xml;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>One named-item match for a search query.</summary>
public sealed record SearchMatch(string Kind, string SystemPath, string Name, string? MatchedAlias);

/// <summary>One place a parameter is referenced.</summary>
public sealed record UsageMatch(string Kind, string Location, string Detail);

/// <summary>
/// Database-wide queries (issue #53 — the ergonomics counterpart of a third-party XTCE toolkit' glob
/// search and parameter-usage lookups): name- and alias-aware search over every named
/// item kind, and "where used" for a parameter across containers, messages, restriction
/// criteria, and preserved command-side fragments.
/// </summary>
public static class XtceDocumentQuery
{
    /// <summary>
    /// Finds named items whose name or alias matches the query: glob when it contains
    /// '*'/'?', case-insensitive substring otherwise. Aliases come from AliasSet fragments
    /// preserved on the item.
    /// </summary>
    public static IReadOnlyList<SearchMatch> Search(SpaceSystem root, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }
        var matcher = BuildMatcher(query);
        var matches = new List<SearchMatch>();

        foreach (var context in SpaceSystemContext.Build(root).SelfAndDescendants())
        {
            var telemetry = context.Node.TelemetryMetaData;
            foreach (var parameter in telemetry?.ParameterSet ?? [])
            {
                AddIfMatched(matches, matcher, "Parameter", context.Path, parameter.Name, parameter.Preserved);
            }
            foreach (var type in telemetry?.ParameterTypeSet ?? [])
            {
                AddIfMatched(matches, matcher, "ParameterType", context.Path, type.Name, type.Preserved);
            }
            foreach (var container in telemetry?.ContainerSet ?? [])
            {
                AddIfMatched(matches, matcher, "Container", context.Path, container.Name, container.Preserved);
            }
            foreach (var message in telemetry?.MessageSet?.Messages ?? [])
            {
                AddIfMatched(matches, matcher, "Message", context.Path, message.Name, message.Preserved);
            }
            foreach (var metaCommand in context.Node.CommandMetaData?.MetaCommands ?? [])
            {
                AddIfMatched(matches, matcher, "MetaCommand", context.Path, metaCommand.Name, metaCommand.Preserved);
            }
        }

        return matches;
    }

    /// <summary>
    /// Every reference to the parameter named <paramref name="parameterName"/> defined in
    /// the system at <paramref name="systemPath"/> (a context path like "Root/Bus"):
    /// modeled container entries, raw entries and preserved fragments carrying a
    /// parameterRef, and restriction-criteria comparisons — each resolved through
    /// NameReferenceResolver from its own scope so only references that actually bind to
    /// THIS parameter count (not same-named parameters elsewhere).
    /// </summary>
    public static IReadOnlyList<UsageMatch> FindParameterUsages(SpaceSystem root, string systemPath, string parameterName)
    {
        var usages = new List<UsageMatch>();

        foreach (var context in SpaceSystemContext.Build(root).SelfAndDescendants())
        {
            var telemetry = context.Node.TelemetryMetaData;

            foreach (var container in telemetry?.ContainerSet ?? [])
            {
                var location = $"{context.Path}/ContainerSet/{container.Name}";
                foreach (var entry in container.EntryList)
                {
                    // Raw entries (ArrayParameterRefEntry, segments, ...) are covered by
                    // the FragmentEnumerator sweep below; scanning them here would
                    // double-count.
                    if (entry.Kind == SequenceEntryKind.ParameterRef && entry.Ref is { } reference
                        && ResolvesToTarget(context, reference, systemPath, parameterName))
                    {
                        usages.Add(new UsageMatch("ParameterRefEntry", location, reference));
                    }
                }

                foreach (var comparison in CriteriaComparisons(container))
                {
                    if (ResolvesToTarget(context, comparison.ParameterRef, systemPath, parameterName))
                    {
                        usages.Add(new UsageMatch("RestrictionComparison", location, comparison.ParameterRef));
                    }
                }
            }

            // Everything else that can carry a parameterRef rides as preserved XML:
            // message MatchCriteria, verifier comparisons, ParameterToSets, alarms,
            // dynamic values, time associations...
            foreach (var (fragment, location) in FragmentEnumerator.EnumerateNode(context))
            {
                if (fragment.ElementName != CommentAnchor.ElementName)
                {
                    AddFragmentUsages(usages, context, fragment.OuterXml, location, systemPath, parameterName);
                }
            }
        }

        return usages;
    }

    // ---- internals ------------------------------------------------------------------------

    private static IEnumerable<Comparison> CriteriaComparisons(SequenceContainer container)
    {
        var criteria = container.BaseContainer?.RestrictionCriteria;
        if (criteria is null)
        {
            yield break;
        }
        if (criteria.Comparison is { } single)
        {
            yield return single;
        }
        foreach (var comparison in criteria.ComparisonList ?? [])
        {
            yield return comparison;
        }
    }

    private static void AddFragmentUsages(
        List<UsageMatch> usages,
        SpaceSystemContext context,
        string outerXml,
        string location,
        string systemPath,
        string parameterName)
    {
        foreach (var (elementName, reference) in FindParameterRefs(outerXml))
        {
            if (ResolvesToTarget(context, reference, systemPath, parameterName))
            {
                usages.Add(new UsageMatch(elementName, location, reference));
            }
        }
    }

    private static bool ResolvesToTarget(SpaceSystemContext scope, string reference, string systemPath, string parameterName)
    {
        var resolution = NameReferenceResolver.Resolve(scope, reference, NamedItemKind.Parameter);
        return resolution.Parameter is { } parameter
            && parameter.Name == parameterName
            && resolution.DefinedIn?.Path == systemPath;
    }

    /// <summary>(elementName, parameterRef) for every element in the fragment carrying a parameterRef attribute.</summary>
    private static IReadOnlyList<(string ElementName, string ParameterRef)> FindParameterRefs(string outerXml)
    {
        var references = new List<(string, string)>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.GetAttribute("parameterRef") is { } reference)
                {
                    references.Add((reader.LocalName, reference));
                }
            }
        }
        catch (XmlException)
        {
            // Malformed preserved content contributes nothing.
        }
        return references;
    }

    private static void AddIfMatched(
        List<SearchMatch> matches,
        Func<string, bool> matcher,
        string kind,
        string systemPath,
        string name,
        IReadOnlyList<RawXmlFragment>? preserved)
    {
        if (matcher(name))
        {
            matches.Add(new SearchMatch(kind, systemPath, name, null));
            return;
        }
        foreach (var alias in FindAliases(preserved))
        {
            if (matcher(alias))
            {
                matches.Add(new SearchMatch(kind, systemPath, name, alias));
                return;
            }
        }
    }

    private static IEnumerable<string> FindAliases(IReadOnlyList<RawXmlFragment>? preserved)
    {
        foreach (var fragment in preserved ?? [])
        {
            if (fragment.ElementName != "AliasSet")
            {
                continue;
            }
            foreach (var (elementName, aliasXml) in ArgumentScanner.ChildElements(fragment.OuterXml))
            {
                if (elementName == "Alias" && XmlFragmentInspector.RootAttribute(aliasXml, "alias") is { } alias)
                {
                    yield return alias;
                }
            }
        }
    }

    private static Func<string, bool> BuildMatcher(string query)
    {
        if (query.Contains('*') || query.Contains('?'))
        {
            var pattern = "^" + Regex.Escape(query).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return regex.IsMatch;
        }
        return candidate => candidate.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
