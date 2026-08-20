using System.Text.Json;
using System.Text.Json.Serialization;
using Xtce.Workshop.Model;
using Xtce.Workshop.Validation;

namespace Xtce.Workshop.Cli;

/// <summary>
/// The `validate` command's logic, separated from Program's arg dispatch so tests can
/// drive it with captured writers. Exit codes: 0 = no findings, 1 = findings reported,
/// 2 = unusable input (missing/unreadable file, malformed XML).
/// </summary>
public static class ValidateCommand
{
    public const int ExitValid = 0;
    public const int ExitFindings = 1;
    public const int ExitError = 2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static int Run(string filePath, bool json, TextWriter output, TextWriter errorOutput)
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
            if (ex is XtceParseException)
            {
                LoadFailure.Describe(filePath, errorOutput);
            }
            return ExitError;
        }

        var issues = XtceValidator.Validate(document);

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(new { validationIssues = issues }, JsonOptions));
        }
        else if (issues.Count == 0)
        {
            output.WriteLine($"{filePath}: no findings.");
        }
        else
        {
            foreach (var issue in issues)
            {
                output.WriteLine($"{issue.Severity.ToString().ToLowerInvariant()} {issue.RuleId} @ {issue.Location}: {issue.Message}");
            }
            output.WriteLine($"{issues.Count} finding(s).");
        }

        return issues.Count == 0 ? ExitValid : ExitFindings;
    }
}
