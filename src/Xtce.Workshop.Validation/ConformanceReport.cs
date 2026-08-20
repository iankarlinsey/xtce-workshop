using System.Reflection;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>How one of the 109 extracted candidates was handled for a given document.</summary>
public enum CandidateStatus
{
    /// <summary>The candidate's check executed against this document and produced no findings.</summary>
    Pass,

    /// <summary>The candidate's check executed and produced findings (listed on the row).</summary>
    Fail,

    /// <summary>Enforced by XML Schema itself; the document passed full XSD validation.</summary>
    SchemaPass,

    /// <summary>Enforced by XML Schema itself; the document FAILED XSD validation (see schema errors).</summary>
    SchemaFail,

    /// <summary>A real semantic rule whose site isn't statically reachable yet (reason on the row).</summary>
    NotEvaluated,

    /// <summary>Triaged as non-normative (descriptive/runtime/display); no static check exists by design.</summary>
    NotApplicable,

    /// <summary>A recorded spec-internal finding, reported for awareness, not checked.</summary>
    Info,
}

public sealed record CandidateReportRow(
    int CandidateNumber,
    string OwnerPath,
    string Disposition,
    string? RuleId,
    CandidateStatus Status,
    IReadOnlyList<ValidationIssue> Findings,
    string Notes);

public sealed record RuleReportRow(string RuleId, bool Executed, int FindingCount);

public sealed record ConformanceReport(
    bool SchemaValid,
    IReadOnlyList<string> SchemaErrors,
    IReadOnlyList<CandidateReportRow> Candidates,
    IReadOnlyList<RuleReportRow> Rules,
    IReadOnlyDictionary<string, int> Summary);

/// <summary>
/// Builds the per-candidate conformance report (issue #48): every one of the 109 Phase A
/// candidates gets an explicit, code-derived result — semantic candidates from executing
/// the tagged rule checks, REDUNDANT candidates from actually running XSD validation, and
/// non-checkable candidates listed with their recorded rationale so nothing is silently
/// absorbed into a single "valid" verdict. The green-book rules (no XSD candidate numbers)
/// appear as rule-level rows.
/// </summary>
public static class ConformanceReportBuilder
{
    /// <summary>SEMANTIC candidates whose sites the current implementation actually checks.</summary>
    private static readonly IReadOnlyDictionary<int, string> CoveredByTag = new Dictionary<int, string>
    {
        [3] = "XTCE-1.2-R22-fixedvalue-bitlength-sufficient",
        [5] = "XTCE-1.2-R02-array-dim-count-match-type",
        [6] = "XTCE-1.2-R05-dim-subset-lt-type",
        [10] = "XTCE-1.2-R04-container-segments-no-overlap",
        [12] = "XTCE-1.2-R08-location-in-container-flags",
        [13] = "XTCE-1.2-R04-container-segments-no-overlap",
        [16] = "XTCE-1.2-R09-messagetype-containerref-must-be-root",
        [19] = "XTCE-1.2-R10-nextcontainer-ref-must-resolve",
        [27] = "XTCE-1.2-R23-constant-datasource-should-be-readonly",
        [29] = "XTCE-1.2-R15-typed-value-valid-for-type",
        [48] = "XTCE-1.2-R12-no-duplicate-verifiers-post-inheritance",
        [49] = "XTCE-1.2-R03-checksum-custom-requires-inputalgorithm",
        [55] = "XTCE-1.2-R13-spline-order-requires-min-points",
        [59] = "XTCE-1.2-R14-time-datatype-requires-encoding",
        [61] = "XTCE-1.2-R06-dimensionlist-order-must-ascend",
        [63] = "XTCE-1.2-R07-enum-initial-value-must-be-valid-label",
        [88] = "XTCE-1.2-R15-typed-value-valid-for-type",
        [91] = "XTCE-1.2-R11-no-dangling-name-references",
        [106] = "XTCE-1.2-R01-ambiguous-time-units-flagged",
    };

    /// <summary>SEMANTIC candidates whose sites are not statically reachable yet, with why.</summary>
    private static readonly IReadOnlyDictionary<int, string> UnreachableReasons = new Dictionary<int, string>
    {
        [1] = "Argument-side array dimensions — command arguments are not modeled yet (R05's recorded partial gap).",
        [2] = "Argument-side array dimensions — command arguments are not modeled yet (R05's recorded partial gap).",
        [33] = "ArgumentAssignment values — command arguments are not modeled yet (R15's recorded partial gap).",
        [34] = "Argument comparison values — command arguments are not modeled yet (R15's recorded partial gap).",
        [35] = "Argument comparison-check values — command arguments are not modeled yet (R15's recorded partial gap).",
        [39] = "Argument initial values — command arguments are not modeled yet (R15's recorded partial gap).",
        [45] = "ParameterToSet values — command-side parameter setting is not modeled yet (R15's recorded partial gap).",
        [62] = "Argument enumerated types — command arguments are not modeled yet (R07's recorded partial gap).",
        [85] = "Verifier comparison-check values ride as preserved verifier XML — not statically inspected (R15's recorded partial gap).",
    };

    /// <summary>SEMANTIC candidates honored structurally rather than by an emission site.</summary>
    private static readonly IReadOnlyDictionary<int, string> StructuralNotes = new Dictionary<int, string>
    {
        [82] = "Aliases never join the reference namespaces, so a reference matching only an alias is already flagged as dangling — violations surface as candidate #91 findings.",
    };

    public static IReadOnlyList<TriageCandidate> Candidates => TriageLog.Value;

    private static readonly Lazy<IReadOnlyList<TriageCandidate>> TriageLog = new(() =>
    {
        using var stream = typeof(ConformanceReportBuilder).Assembly.GetManifestResourceStream("TriageLog.csv")
            ?? throw new InvalidOperationException("Embedded TriageLog.csv missing.");
        using var reader = new StreamReader(stream);
        var records = CsvParser.Parse(reader.ReadToEnd());
        var header = records[0];
        int Col(string name) => Array.IndexOf(header, name);
        return records.Skip(1)
            .Select(f => new TriageCandidate(
                int.Parse(f[Col("CandidateNumber")]),
                f[Col("OwnerPath")],
                f[Col("Status")],
                f[Col("RuleId")],
                f[Col("Reason")]))
            .ToList();
    });

    public sealed record TriageCandidate(int Number, string OwnerPath, string Disposition, string RuleId, string Reason);

    public static ConformanceReport Build(SpaceSystem document)
    {
        var xml = XtceDocumentWriter.Write(document);
        var schemaErrors = SchemaValidator.Validate(xml);
        var schemaValid = schemaErrors.Count == 0;

        var issues = XtceValidator.Validate(document);
        var byCandidate = issues
            .Where(i => i.CandidateNumber is not null)
            .ToLookup(i => i.CandidateNumber!.Value);

        var rows = new List<CandidateReportRow>();
        foreach (var candidate in TriageLog.Value)
        {
            rows.Add(candidate.Disposition switch
            {
                "SEMANTIC" when CoveredByTag.TryGetValue(candidate.Number, out var ruleId) =>
                    MakeExecutedRow(candidate, ruleId, byCandidate[candidate.Number].ToList()),
                "SEMANTIC" when UnreachableReasons.TryGetValue(candidate.Number, out var reason) =>
                    new CandidateReportRow(candidate.Number, candidate.OwnerPath, candidate.Disposition,
                        NullIfEmpty(candidate.RuleId), CandidateStatus.NotEvaluated, [], reason),
                "SEMANTIC" when StructuralNotes.TryGetValue(candidate.Number, out var note) =>
                    new CandidateReportRow(candidate.Number, candidate.OwnerPath, candidate.Disposition,
                        NullIfEmpty(candidate.RuleId), CandidateStatus.Pass, [], note),
                "SEMANTIC" =>
                    throw new InvalidOperationException(
                        $"SEMANTIC candidate #{candidate.Number} has no coverage entry — the coverage registry is incomplete."),
                "REDUNDANT" =>
                    new CandidateReportRow(candidate.Number, candidate.OwnerPath, candidate.Disposition, null,
                        schemaValid ? CandidateStatus.SchemaPass : CandidateStatus.SchemaFail, [],
                        "Enforced by XML Schema; executed via full XSD validation of this document."),
                "NON_NORMATIVE" =>
                    new CandidateReportRow(candidate.Number, candidate.OwnerPath, candidate.Disposition, null,
                        CandidateStatus.NotApplicable, [], candidate.Reason),
                "FLAGGED" =>
                    new CandidateReportRow(candidate.Number, candidate.OwnerPath, candidate.Disposition, null,
                        CandidateStatus.Info, [], candidate.Reason),
                _ => throw new InvalidOperationException($"Unknown disposition '{candidate.Disposition}'."),
            });
        }

        var ruleRows = XtceValidator.RuleIds
            .Select(ruleId => new RuleReportRow(ruleId, true, issues.Count(i => i.RuleId == ruleId)))
            .ToList();

        var summary = rows.GroupBy(r => r.Status).ToDictionary(g => g.Key.ToString(), g => g.Count());

        return new ConformanceReport(schemaValid, schemaErrors, rows, ruleRows, summary);
    }

    private static CandidateReportRow MakeExecutedRow(TriageCandidate candidate, string ruleId, List<ValidationIssue> findings) =>
        new(candidate.Number, candidate.OwnerPath, candidate.Disposition, ruleId,
            findings.Count == 0 ? CandidateStatus.Pass : CandidateStatus.Fail, findings,
            findings.Count == 0 ? "Check executed; no findings at this site." : $"{findings.Count} finding(s) at this site.");

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Minimal RFC 4180 CSV parser (quoted fields, embedded commas/quotes/newlines).</summary>
    private static class CsvParser
    {
        public static List<string[]> Parse(string text)
        {
            var records = new List<string[]>();
            var fields = new List<string>();
            var field = new System.Text.StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else if (c is '\r' or '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    if (fields.Count > 0 || field.Length > 0)
                    {
                        fields.Add(field.ToString());
                        records.Add(fields.ToArray());
                        fields.Clear();
                        field.Clear();
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            if (fields.Count > 0 || field.Length > 0)
            {
                fields.Add(field.ToString());
                records.Add(fields.ToArray());
            }
            return records;
        }
    }
}
