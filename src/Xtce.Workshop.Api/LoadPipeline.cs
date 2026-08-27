using Microsoft.AspNetCore.Mvc;
using Xtce.Workshop.Model;
using Xtce.Workshop.Validation;

namespace Xtce.Workshop.Api;

/// <summary>One progress beat from the load pipeline, snapshotted for pollers.</summary>
public sealed record LoadJobProgress(string Stage, int Percent, int RuleIndex, int RuleCount);

public sealed record LoadPipelineOutcome(
    string? RootNamespace,
    string? DetectedVersion,
    XtceLoadResult Load,
    IReadOnlyList<SchemaError> SchemaErrors,
    IReadOnlyList<ValidationIssue> ValidationIssues,
    TreeNode? Tree);

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
        TreeNode? tree = null;
        if (load.Document is not null)
        {
            validationIssues = XtceValidator.Validate(
                load.Document,
                progress is null ? null : new SynchronousProgress<(int RuleIndex, int RuleCount)>(r =>
                    progress(new LoadJobProgress("rules", 0, r.RuleIndex, r.RuleCount))),
                cancellationToken);
            tree = TreeNode.FromSpaceSystem(load.Document);
        }

        return new LoadPipelineOutcome(rootNamespace, detectedVersion, load, schemaErrors, validationIssues, tree);
    }

    /// <summary>Maps an outcome to the standard load response (identical for sync and job paths).</summary>
    public static IActionResult ToActionResult(LoadPipelineOutcome outcome)
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
                positions = outcome.Load.Positions,
            });
        }
        return new OkObjectResult(new
        {
            name = outcome.Load.Document.Name,
            tree = outcome.Tree,
            document = outcome.Load.Document,
            validationIssues = outcome.ValidationIssues,
            diagnostics = outcome.Load.Diagnostics,
            schemaErrors = outcome.SchemaErrors,
            rootNamespace = outcome.RootNamespace,
            detectedVersion = outcome.DetectedVersion,
            positions = outcome.Load.Positions,
        });
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
