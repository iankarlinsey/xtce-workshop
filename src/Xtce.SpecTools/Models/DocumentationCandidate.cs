namespace Xtce.SpecTools.Models;

public sealed record DocumentationCandidate(
    string OwnerPath,
    string? OwnerTag,
    int Line,
    string Text,
    IReadOnlyList<string> MatchedKeywords);

public sealed record CandidateExtractionResult(
    string SourceFile,
    int TotalDocumentationNodes,
    int CandidateCount,
    IReadOnlyList<DocumentationCandidate> Candidates);
