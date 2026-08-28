using System.Xml;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// Parses the command-side constructs that still ride as preserved XML —
/// ParameterToSetList and the comparison forms inside constraints/verifiers — into
/// lightweight records for the rules that inspect them. Argument declarations, types,
/// and assignments are modeled (see ModeledArguments); malformed preserved XML
/// contributes nothing rather than failing validation.
/// </summary>
public static class ArgumentScanner
{
    public sealed record ParameterToSetInfo(string ParameterRef, string? NewValue);

    public enum ComparisonForm
    {
        /// <summary>ComparisonType: parameterRef + value attributes, no children (candidate #88's family).</summary>
        Plain,

        /// <summary>ArgumentComparisonType: value attribute + ParameterInstanceRef/ArgumentInstanceRef child (#34).</summary>
        InstanceRef,

        /// <summary>(Argument)ComparisonCheckType: a Condition with an instance-ref LHS and a Value child (#35/#85).</summary>
        ConditionValue,
    }

    public sealed record ComparisonInfo(string? ParameterRef, string? ArgumentRef, string Value, ComparisonForm Form);

    // ---- ParameterToSetList -------------------------------------

    public static IReadOnlyList<ParameterToSetInfo> ScanParameterToSets(MetaCommand metaCommand)
    {
        var list = (metaCommand.Preserved ?? []).FirstOrDefault(f => f.ElementName == "ParameterToSetList");
        if (list is null)
        {
            return [];
        }

        var results = new List<ParameterToSetInfo>();
        foreach (var (elementName, outerXml) in ChildElements(list.OuterXml))
        {
            if (elementName != "ParameterToSet")
            {
                continue;
            }
            var parameterRef = XmlFragmentInspector.RootAttribute(outerXml, "parameterRef");
            if (parameterRef is not null)
            {
                results.Add(new ParameterToSetInfo(parameterRef, ChildElementText(outerXml, "NewValue")));
            }
        }
        return results;
    }

    // ---- Comparison forms -----------------------------------------------------------------

    /// <summary>
    /// Every value-carrying comparison in a fragment, in all three XSD shapes: plain
    /// ComparisonType (parameterRef/value attributes), ArgumentComparisonType (value
    /// attribute + instance-ref child), and (Argument)ComparisonCheckType Conditions
    /// (instance-ref LHS + Value child). Conditions whose right-hand side is another
    /// instance ref carry no literal and are skipped.
    /// </summary>
    public static IReadOnlyList<ComparisonInfo> ScanComparisons(string outerXml)
    {
        var comparisons = new List<ComparisonInfo>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

            // ReadOuterXml() advances the reader itself, so the loop must not Read() again
            // right after a capture — that would skip an adjacent sibling.
            var more = reader.Read();
            while (more)
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    more = reader.Read();
                    continue;
                }

                if (reader.LocalName == "Comparison")
                {
                    var subtree = reader.ReadOuterXml();
                    if (XmlFragmentInspector.RootAttribute(subtree, "value") is { } value)
                    {
                        var (parameterRef, argumentRef) = FirstInstanceRef(subtree);
                        if (parameterRef is not null || argumentRef is not null)
                        {
                            comparisons.Add(new ComparisonInfo(parameterRef, argumentRef, value, ComparisonForm.InstanceRef));
                        }
                        else if (XmlFragmentInspector.RootAttribute(subtree, "parameterRef") is { } plainRef)
                        {
                            comparisons.Add(new ComparisonInfo(plainRef, null, value, ComparisonForm.Plain));
                        }
                    }
                    more = reader.NodeType != XmlNodeType.None;
                }
                else if (reader.LocalName == "Condition")
                {
                    var subtree = reader.ReadOuterXml();
                    // A null Value means the right-hand side is another instance ref — no literal to check.
                    if (ChildElementText(subtree, "Value") is { } value)
                    {
                        var (parameterRef, argumentRef) = FirstInstanceRef(subtree);
                        if (parameterRef is not null || argumentRef is not null)
                        {
                            comparisons.Add(new ComparisonInfo(parameterRef, argumentRef, value, ComparisonForm.ConditionValue));
                        }
                    }
                    more = reader.NodeType != XmlNodeType.None;
                }
                else
                {
                    more = reader.Read();
                }
            }
        }
        catch (XmlException)
        {
            // Malformed preserved content contributes nothing rather than failing validation.
        }
        return comparisons;
    }

    /// <summary>Every fragment belonging to one MetaCommand (constraints, verifiers, container internals).</summary>
    public static IEnumerable<RawXmlFragment> CommandFragments(MetaCommand metaCommand)
    {
        foreach (var fragment in metaCommand.Preserved ?? [])
        {
            yield return fragment;
        }
        foreach (var fragment in metaCommand.BaseMetaCommandPreserved ?? [])
        {
            yield return fragment;
        }
        foreach (var verifier in metaCommand.Verifiers ?? [])
        {
            // Modeled verifiers keep their unmodeled check forms (BooleanExpression,
            // CustomAlgorithm, ...) in Preserved; opaque entries ride whole.
            if (verifier.RawXml is { } rawVerifier)
            {
                yield return rawVerifier;
            }
            foreach (var fragment in verifier.Preserved ?? [])
            {
                yield return fragment;
            }
        }
        foreach (var fragment in metaCommand.CommandContainer?.Preserved ?? [])
        {
            yield return fragment;
        }
        foreach (var fragment in metaCommand.CommandContainer?.BaseContainerPreserved ?? [])
        {
            yield return fragment;
        }
        foreach (var entry in metaCommand.CommandContainer?.EntryList ?? [])
        {
            // Modeled entries keep their IncludeConditions etc. in Preserved; Raw entries
            // ride whole — both were reachable when EntryList was one big fragment.
            if (entry.RawXml is { } rawEntry)
            {
                yield return rawEntry;
            }
            foreach (var fragment in entry.Preserved ?? [])
            {
                yield return fragment;
            }
        }
    }

    // ---- shared parsing helpers -------------------------------------------------------------

    /// <summary>(elementName, outerXml) for each direct child element of the fragment's root.</summary>
    public static IReadOnlyList<(string ElementName, string OuterXml)> ChildElements(string outerXml)
    {
        var children = new List<(string, string)>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

            // ReadOuterXml() advances the reader itself — Read() right after it would skip
            // an adjacent sibling element.
            var more = reader.Read();
            while (more)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1)
                {
                    children.Add((reader.LocalName, reader.ReadOuterXml()));
                    more = reader.NodeType != XmlNodeType.None;
                }
                else
                {
                    more = reader.Read();
                }
            }
        }
        catch (XmlException)
        {
            // Malformed preserved content contributes nothing rather than failing validation.
        }
        return children;
    }

    /// <summary>Text content of the first direct child element with the given name, or null.</summary>
    public static string? ChildElementText(string outerXml, string elementName) =>
        ChildElements(outerXml).FirstOrDefault(c => c.ElementName == elementName) is { OuterXml: { } childXml }
        && childXml.Length > 0
            ? XmlFragmentInspector.RootText(childXml)
            : null;

    private static (string? ParameterRef, string? ArgumentRef) FirstInstanceRef(string outerXml)
    {
        foreach (var (elementName, childXml) in ChildElements(outerXml))
        {
            if (elementName == "ParameterInstanceRef")
            {
                return (XmlFragmentInspector.RootAttribute(childXml, "parameterRef"), null);
            }
            if (elementName == "ArgumentInstanceRef")
            {
                return (null, XmlFragmentInspector.RootAttribute(childXml, "argumentRef"));
            }
        }
        return (null, null);
    }
}
