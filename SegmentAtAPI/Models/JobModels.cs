using System.Threading.Channels;

namespace SegmentAPI.Models;

public enum DownloadJobStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
}

/// <summary>
/// Progress snapshot for a segment-download job. Reported both continuously
/// while the current segment is being cut (via ffmpeg's own progress output)
/// and at each segment boundary once a segment finishes.
///
/// OverallFraction combines "how many segments are fully done" with "how far
/// into the current segment we are", so the reported number moves smoothly
/// across the whole job instead of jumping only between segments.
/// </summary>
public record SegmentProgress(int Completed, int Total, string CurrentSegmentName, double OverallFraction);

/// <summary>
/// Tracks one in-flight (or finished) download job — either a segment job or
/// a single full-video job — so its progress can be streamed to the client
/// over SSE and its result fetched once ready.
///
/// The job outlives the HTTP request that created it — it runs on a
/// background task — so all state here needs to be safe to read/write from
/// both that background task and whatever request thread is polling it.
/// </summary>
public class DownloadJob
{
    public Guid Id { get; } = Guid.NewGuid();
    public volatile DownloadJobStatus Status = DownloadJobStatus.Pending;

    public int TotalSegments { get; set; }
    public int CompletedSegments { get; set; }
    public string? CurrentSegmentName { get; set; }

    public string? ResultFilePath { get; set; }
    public string? ResultFileName { get; set; }
    public string? ResultContentType { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    /// <summary>
    /// The JSON payload of the last event written for this job. Used to
    /// "replay" current state to a client that subscribes late (or
    /// reconnects) without needing to know the shape of that payload —
    /// segment jobs and single-video jobs report different fields, so this
    /// keeps the SSE endpoint itself payload-agnostic.
    /// </summary>
    public string? LastEventJson { get; set; }

    // Each SSE connection watching this job reads from this channel.
    // Unbounded because progress messages are small and frequent-but-cheap,
    // and we never want a slow/absent reader to block the worker.
    public Channel<string> Events { get; } = Channel.CreateUnbounded<string>();
}