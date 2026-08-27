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

/// <summary>Source position (1-based) of a modeled element in the loaded text.</summary>
public sealed record LoadPosition(int Line, int Column);

/// <summary>
/// Outcome of a best-effort load: Document is null only when nothing could be loaded at
/// all (malformed XML, unusable root); otherwise it is the full or partial document with
/// every unparseable element quarantined as a preserved fragment (round-trips verbatim)
/// and one diagnostic per problem. Positions index modeled elements by the validator's
/// location grammar ({systemPath}/ParameterSet/{name}, ...) so findings can be mapped
/// back onto the source text.
/// </summary>
public sealed record XtceLoadResult(
    SpaceSystem? Document,
    IReadOnlyList<LoadDiagnostic> Diagnostics,
    IReadOnlyDictionary<string, LoadPosition>? Positions = null);
