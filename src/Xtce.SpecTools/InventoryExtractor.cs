using System.Xml.Linq;
using Xtce.SpecTools.Models;

namespace Xtce.SpecTools;

public static class InventoryExtractor
{
    public static StructuralInventory Extract(string xsdPath)
    {
        var doc = XsdWalker.Load(xsdPath);
        var xs = XsdWalker.Xs;
        var allNodes = doc.Descendants().ToList();

        var elements = NamedOf(allNodes, xs + "element");
        var attributes = NamedOf(allNodes, xs + "attribute");
        var complexTypes = NamedOf(allNodes, xs + "complexType");
        var simpleTypes = NamedOf(allNodes, xs + "simpleType");
        var keys = NamedOf(allNodes, xs + "key");
        var keyRefs = NamedOf(allNodes, xs + "keyref");
        var uniques = NamedOf(allNodes, xs + "unique");

        var enumerations = allNodes
            .Where(n => n.Name == xs + "enumeration")
            .Select(n => new EnumerationConstraint(
                XsdWalker.NearestNamedAncestor(n.Parent ?? n)?.Attribute("name")?.Value,
                n.Attribute("value")?.Value ?? string.Empty,
                XsdWalker.LineOf(n)))
            .ToList();

        var patterns = allNodes
            .Where(n => n.Name == xs + "pattern")
            .Select(n => new PatternConstraint(
                XsdWalker.NearestNamedAncestor(n.Parent ?? n)?.Attribute("name")?.Value,
                n.Attribute("value")?.Value ?? string.Empty,
                XsdWalker.LineOf(n)))
            .ToList();

        var occursConstraints = allNodes
            .Where(n => n.Attribute("minOccurs") is not null || n.Attribute("maxOccurs") is not null)
            .Select(n => new OccursConstraint(
                n.Attribute("name")?.Value ?? XsdWalker.NearestNamedAncestor(n)?.Attribute("name")?.Value,
                n.Attribute("minOccurs")?.Value,
                n.Attribute("maxOccurs")?.Value,
                XsdWalker.LineOf(n)))
            .ToList();

        var refTypedNodes = allNodes
            .Where(n =>
            {
                var name = n.Attribute("name")?.Value ?? n.Attribute("type")?.Value;
                return name is not null && name.Contains("Ref", StringComparison.Ordinal);
            })
            .Select(n => new NamedNode(
                n.Attribute("name")?.Value ?? n.Attribute("type")?.Value ?? string.Empty,
                XsdWalker.LineOf(n)))
            .DistinctBy(n => n.Name)
            .ToList();

        return new StructuralInventory(
            SourceFile: Path.GetFileName(xsdPath),
            TotalNodes: allNodes.Count,
            Elements: elements,
            Attributes: attributes,
            ComplexTypes: complexTypes,
            SimpleTypes: simpleTypes,
            Enumerations: enumerations,
            Patterns: patterns,
            OccursConstraints: occursConstraints,
            Keys: keys,
            KeyRefs: keyRefs,
            Uniques: uniques,
            RefTypedNodes: refTypedNodes);
    }

    private static List<NamedNode> NamedOf(List<XElement> allNodes, XName tag) =>
        allNodes
            .Where(n => n.Name == tag)
            .Select(n => new NamedNode(n.Attribute("name")?.Value ?? "(anonymous)", XsdWalker.LineOf(n)))
            .ToList();
}
