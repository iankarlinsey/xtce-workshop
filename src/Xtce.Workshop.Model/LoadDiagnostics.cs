namespace Xtce.Workshop.Model;

public enum LoadDiagnosticKind
{
    /// <summary>The input is not well-formed XML — parsing cannot continue past this point.</summary>
    MalformedXml,

    /// <summary>A modeled element could not be parsed; in recovery mode it was quarantined verbatim.</summary>
    ModelError,
}

/// <summary>One load problem, positioned as precisely as the reader knows.</summary>
public sealed record LoadDiagnostic(
    LoadDiagnosticKind Kind,
    string Message,
    string Path,
    int? Line,
    int? Column);

/// <summary>
/// Outcome of a best-effort load: Document is null only when nothing could be loaded at
/// all (malformed XML, unusable root); otherwise it is the full or partial document with
/// every unparseable element quarantined as a preserved fragment (round-trips verbatim)
/// and one diagnostic per problem.
/// </summary>
public sealed record XtceLoadResult(SpaceSystem? Document, IReadOnlyList<LoadDiagnostic> Diagnostics);
