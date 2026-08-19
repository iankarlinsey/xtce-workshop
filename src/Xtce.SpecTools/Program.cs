using System.Text.Json;
using Xtce.SpecTools;

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0];
var options = ParseOptions(args.Skip(1));

if (!options.TryGetValue("xsd", out var xsdPath))
{
    Console.Error.WriteLine("Missing required --xsd <path>");
    return 1;
}

switch (command)
{
    case "inventory":
    {
        var result = InventoryExtractor.Extract(xsdPath);
        WriteOutput(result, options, jsonOptions);
        Console.WriteLine(
            $"elements={result.Elements.Count} attributes={result.Attributes.Count} " +
            $"complexTypes={result.ComplexTypes.Count} simpleTypes={result.SimpleTypes.Count} " +
            $"enumerations={result.Enumerations.Count} patterns={result.Patterns.Count} " +
            $"occursConstraints={result.OccursConstraints.Count} keys={result.Keys.Count} " +
            $"keyRefs={result.KeyRefs.Count} uniques={result.Uniques.Count} " +
            $"refTypedNodes={result.RefTypedNodes.Count} totalNodes={result.TotalNodes}");
        return 0;
    }
    case "candidates":
    {
        var result = CandidateExtractor.Extract(xsdPath);
        WriteOutput(result, options, jsonOptions);
        Console.WriteLine(
            $"documentationNodes={result.TotalDocumentationNodes} " +
            $"candidatesWithNormativeLanguage={result.CandidateCount}");
        return 0;
    }
    default:
        PrintUsage();
        return 1;
}

static Dictionary<string, string> ParseOptions(IEnumerable<string> rest)
{
    var map = new Dictionary<string, string>();
    var list = rest.ToList();
    for (var i = 0; i < list.Count - 1; i++)
    {
        if (list[i].StartsWith("--", StringComparison.Ordinal))
            map[list[i][2..]] = list[i + 1];
    }
    return map;
}

static void WriteOutput<T>(T result, Dictionary<string, string> options, JsonSerializerOptions jsonOptions)
{
    var json = JsonSerializer.Serialize(result, jsonOptions);
    if (options.TryGetValue("out", out var outPath))
    {
        File.WriteAllText(outPath, json);
        Console.WriteLine($"Wrote {outPath}");
    }
    else
    {
        Console.WriteLine(json);
    }
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
        Usage:
          xtce-spectools inventory --xsd <path> [--out <path.json>]
          xtce-spectools candidates --xsd <path> [--out <path.json>]
        """);
}
