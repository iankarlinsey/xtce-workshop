namespace Xtce.Workshop.Model;

/// <summary>
/// An unmodeled child element captured verbatim on load (via XmlReader.ReadOuterXml) so it
/// can be written back on save instead of being silently dropped — the core of the lossless
/// round-trip guarantee (issue #23). ElementName is kept separately so the writer can place
/// the fragment in its XSD-sequence-correct slot among modeled siblings.
///
/// Fragments rely on the output document binding the same default namespace as the input
/// (both are XTCE documents, so it does): ReadOuterXml does not add inherited namespace
/// declarations, and WriteRaw re-emits the markup verbatim into the writer's scope. A
/// prefix declared on a modeled ancestor element survives because namespace-declaration
/// attributes are captured as RawAttributes on that element.
///
/// XML comments (issue #51) also travel as fragments, with ElementName == "#comment" and
/// OuterXml holding the comment TEXT (not the &lt;!-- --&gt; markup). Anchor records where
/// the comment sat: the local name of the sibling element it immediately preceded (the
/// writer re-emits it just before that element's slot), CommentAnchor.Leading (emit before
/// the owning element's own start tag — also used for document-prolog comments on the root
/// SpaceSystem), CommentAnchor.Trailing (emit after the owning element's end tag — also
/// used for document-epilog comments on the root), or null (emit at the end of the owning
/// element's children). Anchor is null and meaningless for ordinary element fragments.
/// </summary>
public sealed record RawXmlFragment(string ElementName, string OuterXml, string? Anchor = null);

/// <summary>The special ElementName/Anchor values used by comment fragments (issue #51).</summary>
public static class CommentAnchor
{
    /// <summary>ElementName marking a fragment as a comment (OuterXml = comment text).</summary>
    public const string ElementName = "#comment";

    /// <summary>Emit before the owning element's start tag (document prolog on the root).</summary>
    public const string Leading = "#leading";

    /// <summary>Emit after the owning element's end tag (document epilog on the root).</summary>
    public const string Trailing = "#trailing";
}

/// <summary>
/// An unmodeled attribute captured on load. Name keeps its prefix ("xsi:schemaLocation",
/// "xml:base", "xmlns:xsi") so the output uses the same prefix; NamespaceUri is what the
/// writer actually binds the prefix to. Null NamespaceUri means an unprefixed attribute.
/// </summary>
public sealed record RawAttribute(string Name, string Value, string? NamespaceUri = null);

/// <summary>
/// Structural (contents-based) equality helpers for records with collection-typed
/// properties — the record-generated Equals compares collection instances, not contents
/// (see SpaceSystem.cs for the original gotcha writeup). Null and empty are deliberately
/// distinct: readers produce null when nothing was captured, so round-trip equality holds.
/// </summary>
internal static class Structural
{
    public static bool ListEquals<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.SequenceEqual(b));

    public static void AddList<T>(ref HashCode hash, IReadOnlyList<T>? list)
    {
        if (list is null)
        {
            hash.Add(-1);
            return;
        }

        hash.Add(list.Count);
        foreach (var item in list)
        {
            hash.Add(item);
        }
    }
}
