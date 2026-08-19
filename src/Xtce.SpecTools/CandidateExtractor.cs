using System.Text.RegularExpressions;
using Xtce.SpecTools.Models;

namespace Xtce.SpecTools;

public static partial class CandidateExtractor
{
    // Order matters for readability of MatchedKeywords, not for matching correctness.
    private static readonly string[] NormativeKeywords =
        ["shall not", "shall", "must not", "must", "should not", "should", "required", "may not"];

    public static CandidateExtractionResult Extract(string xsdPath)
    {
        var doc = XsdWalker.Load(xsdPath);
        var xs = XsdWalker.Xs;

        var docNodes = doc.Descendants(xs + "documentation").ToList();

        var candidates = new List<DocumentationCandidate>();
        foreach (var node in docNodes)
        {
            var text = (node.Value ?? string.Empty).Trim();
            if (text.Length == 0)
                continue;

            var matched = NormativeKeywords
                .Where(kw => Regex.IsMatch(text, $@"\b{Regex.Escape(kw)}\b", RegexOptions.IgnoreCase))
                .ToList();

            if (matched.Count == 0)
                continue;

            var owner = node.Parent?.Parent; // documentation -> annotation -> owning construct
            var ownerNamed = owner is not null ? XsdWalker.NearestNamedAncestor(owner) : null;

            candidates.Add(new DocumentationCandidate(
                OwnerPath: ownerNamed is not null ? XsdWalker.OwnerPath(ownerNamed) : "(schema root)",
                OwnerTag: owner?.Name.LocalName,
                Line: XsdWalker.LineOf(node),
                Text: text,
                MatchedKeywords: matched));
        }

        return new CandidateExtractionResult(
            SourceFile: Path.GetFileName(xsdPath),
            TotalDocumentationNodes: docNodes.Count,
            CandidateCount: candidates.Count,
            Candidates: candidates);
    }
}
