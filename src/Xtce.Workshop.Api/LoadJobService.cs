using System.Collections.Concurrent;

namespace Xtce.Workshop.Api;

public sealed record LoadJobSnapshot(
    string State, string Stage, int Percent, int RuleIndex, int RuleCount, string? Error);

/// <summary>
/// Background load jobs for the polling UI: Start runs the shared pipeline on a worker,
/// pollers read atomic snapshots, the result is served once and evicted, and Cancel
/// stops the pipeline server-side. Stale jobs are swept on every interaction.
/// </summary>
public sealed class LoadJobService
{
    private sealed class Job
    {
        public volatile LoadJobSnapshot Snapshot = new("running", "parse", 0, 0, 0, null);
        public readonly CancellationTokenSource Cancellation = new();
        public LoadPipelineOutcome? Outcome;
        public readonly DateTime Created = DateTime.UtcNow;
    }

    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, Job> _jobs = new();

    public string Start(byte[] data)
    {
        Sweep();
        var id = Guid.NewGuid().ToString("n");
        var job = new Job();
        _jobs[id] = job;
        _ = Task.Run(() =>
        {
            try
            {
                var outcome = LoadPipeline.Run(
                    data,
                    p => job.Snapshot = new LoadJobSnapshot("running", p.Stage, p.Percent, p.RuleIndex, p.RuleCount, null),
                    job.Cancellation.Token);
                job.Outcome = outcome;
                job.Snapshot = new LoadJobSnapshot("done", "done", 100, 0, 0, null);
            }
            catch (OperationCanceledException)
            {
                job.Snapshot = new LoadJobSnapshot("cancelled", "cancelled", 0, 0, 0, null);
            }
            catch (Exception ex)
            {
                job.Snapshot = new LoadJobSnapshot("failed", "failed", 0, 0, 0, ex.Message);
            }
        });
        return id;
    }

    public LoadJobSnapshot? GetSnapshot(string id) =>
        _jobs.TryGetValue(id, out var job) ? job.Snapshot : null;

    /// <summary>The finished outcome, served once; the job is evicted with it.</summary>
    public LoadPipelineOutcome? TakeOutcome(string id)
    {
        if (_jobs.TryGetValue(id, out var job) && job.Outcome is not null)
        {
            _jobs.TryRemove(id, out _);
            return job.Outcome;
        }
        return null;
    }

    public bool Cancel(string id)
    {
        if (_jobs.TryGetValue(id, out var job))
        {
            job.Cancellation.Cancel();
            return true;
        }
        return false;
    }

    private void Sweep()
    {
        var cutoff = DateTime.UtcNow - MaxAge;
        foreach (var (id, job) in _jobs)
        {
            if (job.Created < cutoff)
            {
                job.Cancellation.Cancel();
                _jobs.TryRemove(id, out _);
            }
        }
    }
}
