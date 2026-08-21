using System.Text;

namespace Xtce.Workshop.Validation;

/// <summary>
/// Renders a ConformanceReport as human-readable text. Shared by the CLI `report`
/// command and the API's text endpoint so a saved report reads identically everywhere.
/// The header carries document identity so the file is meaningful on its own.
/// </summary>
public static class ConformanceReportRenderer
{
    public static string ToText(ConformanceReport report, string documentName, DateTimeOffset generatedAt, string? declaredNamespace = null)
    {
        var text = new StringBuilder();
        text.AppendLine($"XTCE 1.2 conformance report: {documentName}");
        text.AppendLine($"Generated: {generatedAt.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z");
        if (declaredNamespace is not null)
        {
            text.AppendLine($"Declared namespace: {declaredNamespace}");
        }
        text.AppendLine($"Schema validation: {(report.SchemaValid ? "VALID" : "INVALID")}");
        foreach (var error in report.SchemaErrors)
        {
            text.AppendLine($"  schema: {error}");
        }
        text.AppendLine();
        text.AppendLine($"{"CAND",-5} {"STATUS",-15} {"RULE",-55} OWNER");
        foreach (var row in report.Candidates)
        {
            text.AppendLine($"#{row.CandidateNumber,-4} {Label(row.Status),-15} {row.RuleId ?? "-",-55} {row.OwnerPath}");
            foreach (var finding in row.Findings)
            {
                text.AppendLine($"      -> {finding.Severity.ToString().ToLowerInvariant()} @ {finding.Location}: {finding.Message}");
            }
            if (row.Status is CandidateStatus.NotEvaluated or CandidateStatus.Info)
            {
                text.AppendLine($"      note: {row.Notes}");
            }
        }
        text.AppendLine();
        text.AppendLine("Rules executed:");
        foreach (var rule in report.Rules)
        {
            text.AppendLine($"  {rule.RuleId,-60} {rule.FindingCount} finding(s)");
        }
        text.AppendLine();
        text.AppendLine("Summary: " + string.Join(", ",
            report.Summary.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));
        return text.ToString();
    }

    public static string Label(CandidateStatus status) => status switch
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
