using System.Text.RegularExpressions;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// XTCE-1.2-R12 (documented interpretation — exact-duplicate detection): per
/// VerifierSetType (XSD line 2344), a child MetaCommand's Complete/Execution verifier
/// lists are APPENDED to its BaseMetaCommand's, and "duplicate verifiers in the list of
/// CompleteVerifiers and ExecutionVerifiers before and after appending ... should be
/// avoided". Two verifiers count as duplicates here when their XML is identical after
/// whitespace normalization — semantically-equivalent-but-differently-written verifiers
/// are not detected. The inheritance chain is resolved through the MetaCommand namespace
/// (cycle-guarded); an unresolvable or opaque base means only the locally visible chain
/// is merged.
/// </summary>
public sealed class NoDuplicateVerifiersRule : IValidationRule
{
    public string RuleId => "XTCE-1.2-R12-no-duplicate-verifiers-post-inheritance";
    public ValidationSeverity Severity => ValidationSeverity.Error;

    public IEnumerable<ValidationIssue> Validate(SpaceSystemContext context)
    {
        foreach (var metaCommand in context.Node.CommandMetaData?.MetaCommands ?? [])
        {
            var location = $"{context.Path}/CommandMetaData/MetaCommandSet/{metaCommand.Name}";

            var mergedComplete = MergeInheritedVerifiers(context, metaCommand, "CompleteVerifier");
            foreach (var duplicate in FindDuplicates(mergedComplete))
            {
                yield return new ValidationIssue(RuleId, Severity, location,
                    $"Duplicate CompleteVerifier after resolving BaseMetaCommand inheritance: {duplicate}",
                    CandidateNumber: 48);
            }

            var mergedExecution = MergeInheritedVerifiers(context, metaCommand, "ExecutionVerifier");
            foreach (var duplicate in FindDuplicates(mergedExecution))
            {
                yield return new ValidationIssue(RuleId, Severity, location,
                    $"Duplicate ExecutionVerifier after resolving BaseMetaCommand inheritance: {duplicate}",
                    CandidateNumber: 48);
            }
        }
    }

    /// <summary>Parent-first merged verifier list along the BaseMetaCommand chain.</summary>
    private static List<CommandVerifier> MergeInheritedVerifiers(
        SpaceSystemContext usageContext,
        MetaCommand metaCommand,
        string kind)
    {
        var chain = new List<MetaCommand>();
        var visited = new HashSet<MetaCommand>(ReferenceEqualityComparer.Instance);
        var current = metaCommand;
        var scope = usageContext;

        while (current is not null && visited.Add(current))
        {
            chain.Add(current);
            if (current.BaseMetaCommandRef is not { } baseRef)
            {
                break;
            }
            var resolution = NameReferenceResolver.Resolve(scope, baseRef, NamedItemKind.MetaCommand);
            current = resolution.MetaCommand;
            scope = resolution.DefinedIn ?? scope;
        }

        chain.Reverse(); // parent verifiers come first, per the XSD's append semantics
        return chain
            .SelectMany(m => (m.Verifiers ?? []).Where(v => v.Kind == kind))
            .ToList();
    }

    /// <summary>Structural duplicates — the modeled records compare value-wise, with raw
    /// fragments normalized so formatting differences don't hide a duplicate.</summary>
    private static IEnumerable<string> FindDuplicates(List<CommandVerifier> verifiers)
    {
        var seen = new HashSet<CommandVerifier>();
        var reported = new HashSet<CommandVerifier>();
        foreach (var verifier in verifiers)
        {
            var canonical = Canonicalize(verifier);
            if (!seen.Add(canonical) && reported.Add(canonical))
            {
                yield return Describe(verifier);
            }
        }
    }

    private static CommandVerifier Canonicalize(CommandVerifier verifier)
    {
        var preserved = verifier.Preserved is { Count: > 0 }
            ? verifier.Preserved.Select(f => f with { OuterXml = Normalize(f.OuterXml) }).ToList()
            : verifier.Preserved;
        var rawXml = verifier.RawXml is { } raw ? raw with { OuterXml = Normalize(raw.OuterXml) } : null;
        return verifier with { Preserved = preserved, RawXml = rawXml };
    }

    private static string Describe(CommandVerifier verifier)
    {
        if (verifier.Comparison is { } comparison)
        {
            return $"Comparison {comparison.ParameterRef} {comparison.ComparisonOperator ?? "=="} {comparison.Value}";
        }
        if (verifier.ComparisonList is { Count: > 0 } list)
        {
            return $"ComparisonList ({list.Count} comparison(s), first: {list[0].ParameterRef})";
        }
        if (verifier.ContainerRef is { } containerRef)
        {
            return $"ContainerRef {containerRef}";
        }
        var fragment = verifier.RawXml ?? verifier.Preserved?.FirstOrDefault();
        if (fragment is not null)
        {
            var summary = Normalize(fragment.OuterXml);
            return summary.Length > 120 ? summary[..120] + "…" : summary;
        }
        return verifier.Kind;
    }

    private static string Normalize(string xml) =>
        Regex.Replace(Regex.Replace(xml, @">\s+<", "><"), @"\s+", " ").Trim();
}
