using System.Text.Json;
using System.Text.Json.Serialization;
using Xtce.Workshop.Model;
using Xtce.Workshop.Validation;

namespace Xtce.Workshop.Cli;

/// <summary>
/// The `report` command: the full per-candidate conformance report — one explicit,
/// code-executed result for each of the 109 statements extracted from the XTCE 1.2 XSD,
/// plus real schema validation and per-rule execution results. Exit codes: 0 = no FAIL
/// or SCHEMA_FAIL rows, 1 = at least one, 2 = unusable input.
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
            return ExitError;
        }

        var report = ConformanceReportBuilder.Build(document);

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        }
        else
        {
            WriteText(filePath, report, output);
        }

        var failed = report.Candidates.Any(c => c.Status is CandidateStatus.Fail or CandidateStatus.SchemaFail);
        return failed ? ExitFindings : ExitClean;
    }

    private static void WriteText(string filePath, ConformanceReport report, TextWriter output)
    {
        output.WriteLine($"XTCE 1.2 conformance report: {filePath}");
        output.WriteLine($"Schema validation: {(report.SchemaValid ? "VALID" : "INVALID")}");
        foreach (var error in report.SchemaErrors)
        {
            output.WriteLine($"  schema: {error}");
        }
        output.WriteLine();
        output.WriteLine($"{"CAND",-5} {"STATUS",-15} {"RULE",-55} OWNER");
        foreach (var row in report.Candidates)
        {
            output.WriteLine($"#{row.CandidateNumber,-4} {Label(row.Status),-15} {row.RuleId ?? "-",-55} {row.OwnerPath}");
            foreach (var finding in row.Findings)
            {
                output.WriteLine($"      -> {finding.Severity.ToString().ToLowerInvariant()} @ {finding.Location}: {finding.Message}");
            }
            if (row.Status is CandidateStatus.NotEvaluated or CandidateStatus.Info)
            {
                output.WriteLine($"      note: {row.Notes}");
            }
        }
        output.WriteLine();
        output.WriteLine("Rules executed:");
        foreach (var rule in report.Rules)
        {
            output.WriteLine($"  {rule.RuleId,-60} {rule.FindingCount} finding(s)");
        }
        output.WriteLine();
        output.WriteLine("Summary: " + string.Join(", ",
            report.Summary.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));
    }

    private static string Label(CandidateStatus status) => status switch
    {
        CandidateStatus.Pass => "PASS",
        CandidateStatus.Fail => "FAIL",
        CandidateStatus.SchemaPass => "SCHEMA_PASS",
        CandidateStatus.SchemaFail => "SCHEMA_FAIL",
        CandidateStatus.NotEvaluated => "NOT_EVALUATED",
        CandidateStatus.NotApplicable => "NOT_APPLICABLE",
        CandidateStatus.Info => "INFO",
        _ => status.ToString(),
    };
}
