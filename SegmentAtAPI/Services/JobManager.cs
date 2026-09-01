using System.Collections.Concurrent;
using SegmentAPI.Models;

namespace SegmentAPI.Services;

/// <summary>
/// In-memory registry of in-flight/finished download jobs. Singleton —
/// background tasks and HTTP requests both need to see the same jobs.
///
/// Note: this only works for a single API instance. If this ever runs
/// behind a load balancer with multiple instances, job state would need to
/// move to something shared (e.g. Redis) since a client's SSE connection
/// and its "start" request could land on different instances.
/// </summary>
public class JobManager
{
    private readonly ConcurrentDictionary<Guid, DownloadJob> _jobs = new();

    public DownloadJob Create()
    {
        var job = new DownloadJob();
        _jobs[job.Id] = job;
        return job;
    }

    public DownloadJob? Get(Guid id) => _jobs.TryGetValue(id, out var job) ? job : null;

    public void Remove(Guid id) => _jobs.TryRemove(id, out _);

    /// <summary>
    /// Best-effort sweep for jobs nobody ever came back to collect (e.g. the
    /// browser tab was closed mid-download) so temp zip files and the
    /// dictionary don't grow unbounded.
    /// </summary>
    public void RemoveStaleJobs(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        foreach (var (id, job) in _jobs)
        {
            if (job.CreatedAt >= cutoff) continue;

            if (job.ResultFilePath != null && File.Exists(job.ResultFilePath))
            {
                try { File.Delete(job.ResultFilePath); } catch { /* best-effort */ }
            }

            _jobs.TryRemove(id, out _);
        }
    }
}
