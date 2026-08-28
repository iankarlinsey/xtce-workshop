using System.Text;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// Spreadsheet-friendly CSV exports. RFC 4180 quoting; one header row; empty cells where a value
/// isn't statically known.
/// </summary>
public static class XtceCsvExporter
{
    /// <summary>
    /// One row per telemetry parameter across the whole tree:
    /// SystemPath,Name,ParameterTypeRef,Kind,EncodedSizeInBits,InitialValue,DataSource,Aliases.
    /// Kind/size come from the resolved type (blank when the ref is dangling or opaque);
    /// DataSource from a preserved ParameterProperties fragment; Aliases as
    /// "namespace:alias" pairs joined with "; ".
    /// </summary>
    public static string ExportParameters(SpaceSystem root)
    {
        var csv = new StringBuilder();
        AppendRow(csv, ["SystemPath", "Name", "ParameterTypeRef", "Kind", "EncodedSizeInBits", "InitialValue", "DataSource", "Aliases"]);

        foreach (var context in SpaceSystemContext.Build(root).SelfAndDescendants())
        {
            foreach (var parameter in context.Node.TelemetryMetaData?.ParameterSet ?? [])
            {
                var typeResolution = NameReferenceResolver.Resolve(context, parameter.ParameterTypeRef, NamedItemKind.ParameterType);
                var kind = typeResolution.ParameterType?.Kind.ToString() ?? "";
                var size = typeResolution.ParameterType is { } type
                    ? PacketLayoutBuilder.EncodedSize(type).Size?.ToString() ?? ""
                    : "";
                var propertiesFragment = (parameter.Preserved ?? []).FirstOrDefault(f => f.ElementName == "ParameterProperties");
                var dataSource = parameter.Properties?.DataSource
                    ?? (propertiesFragment is null
                        ? ""
                        : XmlFragmentInspector.RootAttribute(propertiesFragment.OuterXml, "dataSource") ?? "");

                AppendRow(csv,
                [
                    context.Path,
                    parameter.Name,
                    parameter.ParameterTypeRef,
                    kind,
                    size,
                    parameter.InitialValue ?? "",
                    dataSource,
                    Aliases(parameter.Preserved),
                ]);
            }
        }
        return csv.ToString();
    }

    /// <summary>
    /// One row per computed layout entry of every container across the tree:
    /// SystemPath,Container,EntryName,EntryKind,SourceContainer,OffsetInBits,SizeInBits,Note.
    /// Offsets/sizes come from PacketLayoutBuilder and are blank where not statically known.
    /// </summary>
    public static string ExportContainers(SpaceSystem root)
    {
        var csv = new StringBuilder();
        AppendRow(csv, ["SystemPath", "Container", "EntryName", "EntryKind", "SourceContainer", "OffsetInBits", "SizeInBits", "Note"]);

        WalkContainers(root, [], (systemPath, pathName, container) =>
        {
            var layout = PacketLayoutBuilder.Build(root, systemPath, container.Name);
            foreach (var row in layout?.Rows ?? [])
            {
                AppendRow(csv,
                [
                    pathName,
                    container.Name,
                    row.Name,
                    row.Kind,
                    row.SourceContainer,
                    row.OffsetInBits?.ToString() ?? "",
                    row.SizeInBits?.ToString() ?? "",
                    row.Note ?? "",
                ]);
            }
        });
        return csv.ToString();
    }

    private static void WalkContainers(SpaceSystem node, List<int> path, Action<IReadOnlyList<int>, string, SequenceContainer> visit)
    {
        Walk(node, node.Name);
        return;

        void Walk(SpaceSystem current, string pathName)
        {
            foreach (var container in current.TelemetryMetaData?.ContainerSet ?? [])
            {
                visit(path.ToList(), pathName, container);
            }
            for (var i = 0; i < current.Children.Count; i++)
            {
                path.Add(i);
                Walk(current.Children[i], $"{pathName}/{current.Children[i].Name}");
                path.RemoveAt(path.Count - 1);
            }
        }
    }

    private static string Aliases(IReadOnlyList<RawXmlFragment>? preserved)
    {
        var aliases = new List<string>();
        foreach (var fragment in preserved ?? [])
        {
            if (fragment.ElementName != "AliasSet")
            {
                continue;
            }
            foreach (var (elementName, aliasXml) in ArgumentScanner.ChildElements(fragment.OuterXml))
            {
                if (elementName != "Alias")
                {
                    continue;
                }
                var alias = XmlFragmentInspector.RootAttribute(aliasXml, "alias");
                if (alias is null)
                {
                    continue;
                }
                var nameSpace = XmlFragmentInspector.RootAttribute(aliasXml, "nameSpace");
                aliases.Add(nameSpace is null ? alias : $"{nameSpace}:{alias}");
            }
        }
        return string.Join("; ", aliases);
    }

    private static void AppendRow(StringBuilder csv, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                csv.Append(',');
            }
            csv.Append(Quote(fields[i]));
        }
        csv.Append("\r\n");
    }

    private static string Quote(string field) =>
        field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r')
            ? "\"" + field.Replace("\"", "\"\"") + "\""
            : field;
}
