using Microsoft.AspNetCore.Mvc;
using Xtce.Workshop.Model;
using Xtce.Workshop.Validation;

namespace Xtce.Workshop.Api;

/// <summary>One progress beat from the load pipeline, snapshotted for pollers.</summary>
public sealed record LoadJobProgress(string Stage, int Percent, int RuleIndex, int RuleCount, string? RuleId = null);

public sealed record LoadPipelineOutcome(
    string? RootNamespace,
    string? DetectedVersion,
    XtceLoadResult Load,
    IReadOnlyList<SchemaError> SchemaErrors,
    IReadOnlyList<ValidationIssue> ValidationIssues,
    long InputByteCount);

/// <summary>A validation issue with its source position resolved at load time (#90 item 2).</summary>
public sealed record PositionedValidationIssue(
    string RuleId,
    ValidationSeverity Severity,
    string Location,
    string Message,
    int? CandidateNumber,
    int? Line,
    int? Column);

/// <summary>
/// The load pipeline shared by the synchronous endpoints and background jobs:
/// namespace probe, recovery parse, schema validation, rule validation. Progress is
/// byte-accurate for parse and schema (forward-only readers over a reporting stream)
/// and rule-indexed for validation; the token cancels between and inside stages.
/// </summary>
public static class LoadPipeline
{
    public static LoadPipelineOutcome Run(
        byte[] data,
        Action<LoadJobProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var probeStream = new MemoryStream(data);
        var rootNamespace = XtceNamespace.ReadRootNamespace(probeStream);
        var detectedVersion = XtceNamespace.VersionFor(rootNamespace);

        using var parseStream = new ProgressReportingStream(
            new MemoryStream(data),
            progress is null ? null : new SynchronousProgress(p =>
                progress(new LoadJobProgress("parse", (int)(p * 100), 0, 0))),
            cancellationToken);
        var load = XtceDocumentReader.LoadWithRecovery(parseStream);

        cancellationToken.ThrowIfCancellationRequested();
        using var schemaStream = new ProgressReportingStream(
            new MemoryStream(data),
            progress is null ? null : new SynchronousProgress(p =>
                progress(new LoadJobProgress("schema", (int)(p * 100), 0, 0))),
            cancellationToken);
        var schemaErrors = SchemaValidator.ValidateDetailed(schemaStream);
        // A cancelled read can surface wrapped inside the XML readers' own error paths;
        // re-assert cancellation at each stage boundary so it always wins.
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ValidationIssue> validationIssues = [];
        if (load.Document is not null)
        {
            validationIssues = XtceValidator.Validate(
                load.Document,
                progress is null ? null : new SynchronousProgress<(int RuleIndex, int RuleCount, string RuleId)>(r =>
                    progress(new LoadJobProgress("rules", 0, r.RuleIndex, r.RuleCount, r.RuleId))),
                cancellationToken);
        }

        return new LoadPipelineOutcome(rootNamespace, detectedVersion, load, schemaErrors, validationIssues, data.LongLength);
    }

    /// <summary>
    /// Maps an outcome to the standard load response (identical for sync and job paths).
    /// Findings carry their source line/column directly (#90 item 2 — resolved here via
    /// the longest recorded ancestor path); the response ships neither the per-element
    /// positions map nor the redundant tree (#90 item 1) — for large files those
    /// dominated a payload the browser could not hold.
    /// </summary>
    public static IActionResult ToActionResult(
        LoadPipelineOutcome outcome, DocumentSessionService? sessions = null, long largeDocumentThresholdBytes = long.MaxValue)
    {
        if (outcome.Load.Document is null)
        {
            return new BadRequestObjectResult(new
            {
                error = outcome.Load.Diagnostics.FirstOrDefault()?.Message ?? "The file could not be loaded.",
                diagnostics = outcome.Load.Diagnostics,
                schemaErrors = outcome.SchemaErrors,
                rootNamespace = outcome.RootNamespace,
                detectedVersion = outcome.DetectedVersion,
            });
        }
        if (sessions is not null && outcome.InputByteCount >= largeDocumentThresholdBytes)
        {
            // #129 large mode: the browser cannot hold a document this size as JSON —
            // the model stays server-side and the client browses/edits it by item.
            return new OkObjectResult(new
            {
                name = outcome.Load.Document.Name,
                largeDocument = true,
                documentSessionId = sessions.Store(outcome.Load.Document),
                inputByteCount = outcome.InputByteCount,
                validationIssues = PositionIssues(outcome),
                diagnostics = outcome.Load.Diagnostics,
                schemaErrors = outcome.SchemaErrors,
                rootNamespace = outcome.RootNamespace,
                detectedVersion = outcome.DetectedVersion,
            });
        }
        return new OkObjectResult(new
        {
            name = outcome.Load.Document.Name,
            document = outcome.Load.Document,
            validationIssues = PositionIssues(outcome),
            diagnostics = outcome.Load.Diagnostics,
            schemaErrors = outcome.SchemaErrors,
            rootNamespace = outcome.RootNamespace,
            detectedVersion = outcome.DetectedVersion,
        });
    }

    private static IReadOnlyList<PositionedValidationIssue> PositionIssues(LoadPipelineOutcome outcome) =>
        outcome.ValidationIssues
            .Select(issue =>
            {
                var position = ResolveLocation(issue.Location, outcome.Load.Positions);
                return new PositionedValidationIssue(
                    issue.RuleId, issue.Severity, issue.Location, issue.Message, issue.CandidateNumber,
                    position?.Line, position?.Column);
            })
            .ToList();

    /// <summary>
    /// Resolves a validator location ("Sat/ContainerSet/Frame") to a source position,
    /// falling back to the longest recorded ancestor path so a deeper citation still
    /// lands near its owner. (Previously done per-marker in the browser.)
    /// </summary>
    private static LoadPosition? ResolveLocation(string location, IReadOnlyDictionary<string, LoadPosition>? positions)
    {
        if (positions is null)
        {
            return null;
        }
        var candidate = location;
        while (true)
        {
            if (positions.TryGetValue(candidate, out var position))
            {
                return position;
            }
            var cut = candidate.LastIndexOf('/');
            if (cut < 0)
            {
                return null;
            }
            candidate = candidate[..cut];
        }
    }

    /// <summary>Progress<T> posts to a sync context; the pipeline wants inline delivery.</summary>
    private sealed class SynchronousProgress : IProgress<double>
    {
        private readonly Action<double> _handler;
        public SynchronousProgress(Action<double> handler) => _handler = handler;
        public void Report(double value) => _handler(value);
    }

    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SynchronousProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}
