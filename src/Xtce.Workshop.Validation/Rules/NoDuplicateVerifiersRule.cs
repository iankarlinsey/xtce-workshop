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

            var mergedComplete = MergeInheritedVerifiers(context, metaCommand, m => m.CompleteVerifiers);
            foreach (var duplicate in FindDuplicates(mergedComplete))
            {
                yield return new ValidationIssue(RuleId, Severity, location,
                    $"Duplicate CompleteVerifier after resolving BaseMetaCommand inheritance: {duplicate}");
            }

            var mergedExecution = MergeInheritedVerifiers(context, metaCommand, m => m.ExecutionVerifiers);
            foreach (var duplicate in FindDuplicates(mergedExecution))
            {
                yield return new ValidationIssue(RuleId, Severity, location,
                    $"Duplicate ExecutionVerifier after resolving BaseMetaCommand inheritance: {duplicate}");
            }
        }
    }

    /// <summary>Parent-first merged verifier list along the BaseMetaCommand chain.</summary>
    private static List<string> MergeInheritedVerifiers(
        SpaceSystemContext usageContext,
        MetaCommand metaCommand,
        Func<MetaCommand, IReadOnlyList<RawXmlFragment>?> select)
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
            .SelectMany(m => select(m) ?? [])
            .Select(f => Normalize(f.OuterXml))
            .ToList();
    }

    private static IEnumerable<string> FindDuplicates(List<string> normalizedVerifiers)
    {
        var seen = new HashSet<string>();
        var reported = new HashSet<string>();
        foreach (var verifier in normalizedVerifiers)
        {
            if (!seen.Add(verifier) && reported.Add(verifier))
            {
                var summary = verifier.Length > 120 ? verifier[..120] + "…" : verifier;
                yield return summary;
            }
        }
    }

    private static string Normalize(string xml) =>
        Regex.Replace(Regex.Replace(xml, @">\s+<", "><"), @"\s+", " ").Trim();
}
