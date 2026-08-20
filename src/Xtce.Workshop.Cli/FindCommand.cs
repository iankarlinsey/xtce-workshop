using System.Text.Json;
using System.Text.Json.Serialization;
using Xtce.Workshop.Model;
using Xtce.Workshop.Validation;

namespace Xtce.Workshop.Cli;

/// <summary>
/// The `find` command: name/alias search over every named item kind.
/// Exit codes: 0 = at least one match, 1 = no matches, 2 = unusable input.
/// </summary>
public static class FindCommand
{
    public const int ExitFound = 0;
    public const int ExitNoMatches = 1;
    public const int ExitError = 2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static int Run(string filePath, string query, bool json, TextWriter output, TextWriter errorOutput)
    {
        SpaceSystem document;
        try
        {
            using var stream = File.OpenRead(filePath);
            document = XtceDocumentReader.Load(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XtceParseException)
        {
            errorOutput.WriteLine($"error: {ex.Message}");
            return ExitError;
        }

        var matches = XtceDocumentQuery.Search(document, query);

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(new { matches }, JsonOptions));
        }
        else if (matches.Count == 0)
        {
            output.WriteLine($"no matches for '{query}'.");
        }
        else
        {
            foreach (var match in matches)
            {
                var alias = match.MatchedAlias is null ? "" : $" (alias: {match.MatchedAlias})";
                output.WriteLine($"{match.Kind,-14} {match.SystemPath}/{match.Name}{alias}");
            }
            output.WriteLine($"{matches.Count} match(es).");
        }

        return matches.Count > 0 ? ExitFound : ExitNoMatches;
    }
}
