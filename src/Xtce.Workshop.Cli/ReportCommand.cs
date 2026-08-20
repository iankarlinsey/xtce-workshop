using System.Text.Json;
using System.Text.Json.Serialization;
using Xtce.Workshop.Model;
using Xtce.Workshop.Validation;

namespace Xtce.Workshop.Cli;

/// <summary>
/// The `report` command: the full per-candidate conformance report — one explicit,
/// code-executed result for each of the 109 statements extracted from the XTCE 1.2 XSD,
/// plus real schema validation and per-rule execution results. Output goes to stdout, or
/// to a file with --out. Exit codes: 0 = no FAIL or SCHEMA_FAIL rows, 1 = at least one,
/// 2 = unusable input.
/// </summary>
public static class ReportCommand
{
    public const int ExitClean = 0;
    public const int ExitFindings = 1;
    public const int ExitError = 2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static int Run(string filePath, bool json, TextWriter output, TextWriter errorOutput, string? outPath = null)
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

        var report = ConformanceReportBuilder.Build(document);

        var rendered = json
            ? JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine
            : ConformanceReportRenderer.ToText(report, Path.GetFileName(filePath), DateTimeOffset.UtcNow);

        if (outPath is null)
        {
            output.Write(rendered);
        }
        else
        {
            try
            {
                File.WriteAllText(outPath, rendered);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errorOutput.WriteLine($"error: {ex.Message}");
                return ExitError;
            }
            output.WriteLine($"wrote {outPath}");
        }

        var failed = report.Candidates.Any(c => c.Status is CandidateStatus.Fail or CandidateStatus.SchemaFail);
        return failed ? ExitFindings : ExitClean;
    }
}
