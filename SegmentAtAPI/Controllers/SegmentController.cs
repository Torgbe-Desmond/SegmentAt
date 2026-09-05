using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using SegmentAPI.interfaces;
using SegmentAPI.Exceptions;
using SegmentAPI.Models;
using SegmentAPI.Services;
using System.IO.Compression;
using System.Text.Json;
namespace SegmentAPI.Constrollers;

[ApiController]
[Route("/api/v1")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class SegmentController : Controller
{
    private readonly IYoutubeDownloader _youtubeDownloader;
    private readonly JobManager _jobManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SegmentController> _logger;

    private static readonly JsonSerializerOptions JobEventJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public SegmentController(
        IYoutubeDownloader youtubeDownloader,
        JobManager jobManager,
        IServiceScopeFactory scopeFactory,
        ILogger<SegmentController> logger)
    {
        this._youtubeDownloader = youtubeDownloader;
        this._jobManager = jobManager;
        this._scopeFactory = scopeFactory;
        this._logger = logger;
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok();

    [Route("fetch")]
    [HttpPost]
    [ProducesResponseType(typeof(ResponseModel<YoutubeVideo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetVideoInfo([FromBody] FetchVideoRequest request)
    {
        try
        {
            YoutubeVideo? videoInfo = await _youtubeDownloader.GetVideoInfoAsync(request.YoutubeUrl);

            if (videoInfo == null)
            {
                throw new NotFoundException("Video was not found");
            }

            ResponseModel<YoutubeVideo> response = new ResponseModel<YoutubeVideo>
            {
                Data = videoInfo,
                Message = "Video was fetched successfully",
                StatusCode = StatusCodes.Status200OK,
            };

            return Ok(response);
        }
        catch (Exception)
        {
            throw;
        }
    }


    [HttpPost("download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadVideo([FromBody] DownloadStreamRequest request, CancellationToken cancellationToken)
    {
        try
        {
            DownloadResult downloadResult = await _youtubeDownloader.DownloadToWebStreamAsync(request, cancellationToken: cancellationToken);

            if (downloadResult == null)
                throw new BadRequestException("Could not process the download request.");

            string contentType = request.Container?.ToLowerInvariant() switch
            {
                "mp4" => "video/mp4",
                "webm" => "video/webm",
                _ => "application/octet-stream"
            };

            return File(downloadResult.FileStream, contentType, downloadResult.FileName);
        }
        catch (Exception)
        {
            throw;
        }
    }

    /// <summary>
    /// Starts a single full-video download job in the background and
    /// returns immediately with a job id. Reports real, continuous progress
    /// (not just start/done) via YoutubeExplode's own IProgress&lt;double&gt;
    /// support in its downloader — unlike the segment path, no ffmpeg output
    /// parsing is needed here.
    /// </summary>
    [HttpPost("download/start")]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status400BadRequest)]
    public IActionResult StartDownloadVideo([FromBody] DownloadStreamRequest request)
    {
        var job = _jobManager.Create();

        _ = Task.Run(() => RunVideoDownloadJobAsync(job, request));

        return Ok(new ResponseModel<object>
        {
            Data = new { jobId = job.Id },
            Message = "Download started",
            StatusCode = StatusCodes.Status200OK,
        });
    }

    private async Task RunVideoDownloadJobAsync(DownloadJob job, DownloadStreamRequest request)
    {
        job.Status = DownloadJobStatus.Processing;

        using IServiceScope scope = _scopeFactory.CreateScope();
        var downloader = scope.ServiceProvider.GetRequiredService<IYoutubeDownloader>();

        var progress = new Progress<double>(fraction =>
        {
            WriteEvent(job, new
            {
                status = "processing",
                percent = Math.Round(Math.Clamp(fraction, 0.0, 1.0) * 100, 1),
            });
        });

        try
        {
            DownloadResult result = await downloader.DownloadToWebStreamAsync(request, progress);

            if (result == null)
            {
                throw new BadRequestException("Could not process the download request.");
            }

            // result.FileStream is opened with FileOptions.DeleteOnClose, and
            // this job's background lifetime spans a different HTTP request
            // than the one that will eventually fetch it — copy to a stable
            // temp file that survives until /result is called, same pattern
            // as the single-segment case below.
            string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{result.FileName}");
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            {
                await result.FileStream.CopyToAsync(fileStream);
            }
            await result.FileStream.DisposeAsync();

            job.ResultFilePath = tempPath;
            job.ResultFileName = result.FileName;
            job.ResultContentType = request.Container?.ToLowerInvariant() switch
            {
                "mp4" => "video/mp4",
                "webm" => "video/webm",
                _ => "application/octet-stream"
            };

            job.Status = DownloadJobStatus.Completed;
            WriteEvent(job, new { status = "completed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Video download job {JobId} failed", job.Id);
            job.Status = DownloadJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            WriteEvent(job, new { status = "failed", error = ex.Message });
        }
        finally
        {
            job.Events.Writer.TryComplete();
        }
    }

    [HttpPost("download-segments")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadSegments(
    [FromBody] DownloadSegmentsRequest request,
    CancellationToken cancellationToken)
    {
        SegmentsDownloadResult results;

        try
        {
            results = await _youtubeDownloader.DownloadSegmentsAsync(request, cancellationToken: cancellationToken);

            if (results == null || results.Segments.Count == 0)
            {
                throw new BadRequestException("No segments were generated.");
            }
        }
        catch (Exception)
        {
            throw;
        }

        if (results.Segments.Count == 1)
        {
            string contentType = request.Container?.ToLowerInvariant() switch
            {
                "mp4" => "video/mp4",
                "webm" => "video/webm",
                _ => "application/octet-stream"
            };

            return File(results.Segments[0].FileStream, contentType, results.Segments[0].FileName);
        }

        string zipFileName = $"{results.VideoTitle}.zip";

        try
        {
            string tempZipPath = await BuildZipFromSegmentsAsync(results.Segments, cancellationToken);

            var readStream = new FileStream(
                tempZipPath, FileMode.Open, FileAccess.Read, FileShare.None,
                4096, FileOptions.DeleteOnClose | FileOptions.Asynchronous);

            return File(readStream, "application/zip", zipFileName);
        }
        catch (Exception)
        {
            foreach (var result in results.Segments)
            {
                try { await result.FileStream.DisposeAsync(); } catch { }
            }

            throw;
        }
    }

    /// <summary>
    /// Zips the given segment streams to a fresh temp file on disk and
    /// returns its path. Building to disk first (rather than streaming the
    /// archive live over the HTTP response) means a failure partway through
    /// never leaves a client holding a truncated "successful" download —
    /// nothing is sent until the zip is known to be complete and valid.
    /// Disposes each segment's FileStream as it's consumed either way.
    /// </summary>
    private static async Task<string> BuildZipFromSegmentsAsync(
        List<DownloadResult> segments,
        CancellationToken cancellationToken)
    {
        string tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");

        try
        {
            using (var zipFileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write))
            using (var archive = new ZipArchive(zipFileStream, ZipArchiveMode.Create))
            {
                foreach (var result in segments)
                {
                    var zipEntry = archive.CreateEntry(result.FileName, CompressionLevel.Fastest);

                    using (var entryStream = zipEntry.Open())
                    {
                        await result.FileStream.CopyToAsync(entryStream, cancellationToken);
                    }

                    await result.FileStream.DisposeAsync();
                }
            }

            return tempZipPath;
        }
        catch
        {
            if (System.IO.File.Exists(tempZipPath))
            {
                try { System.IO.File.Delete(tempZipPath); } catch { }
            }

            throw;
        }
    }

    /// <summary>
    /// Starts a segment download+zip job in the background and returns
    /// immediately with a job id. Use the /events endpoint below to watch
    /// progress and /result to fetch the finished zip once it's ready.
    /// </summary>
    [HttpPost("download-segments/start")]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status400BadRequest)]
    public IActionResult StartDownloadSegments([FromBody] DownloadSegmentsRequest request)
    {
        var job = _jobManager.Create();
        job.TotalSegments = request.Segments.Count;

        // Fire-and-forget on a background task, deliberately not awaited —
        // the request returns immediately with the job id. This task outlives
        // the HTTP request, so it must not use the request's own DI scope
        // (that gets disposed as soon as this action returns) — a fresh
        // scope is created inside RunJobAsync instead.
        _ = Task.Run(() => RunJobAsync(job, request));

        return Ok(new ResponseModel<object>
        {
            Data = new { jobId = job.Id },
            Message = "Download started",
            StatusCode = StatusCodes.Status200OK,
        });
    }

    private async Task RunJobAsync(DownloadJob job, DownloadSegmentsRequest request)
    {
        job.Status = DownloadJobStatus.Processing;

        using IServiceScope scope = _scopeFactory.CreateScope();
        var downloader = scope.ServiceProvider.GetRequiredService<IYoutubeDownloader>();

        var progress = new Progress<SegmentProgress>(p =>
        {
            job.CompletedSegments = p.Completed;
            job.CurrentSegmentName = p.CurrentSegmentName;
            WriteEvent(job, new
            {
                status = "processing",
                completed = p.Completed,
                total = p.Total,
                current = p.CurrentSegmentName,
                percent = Math.Round(p.OverallFraction * 100, 1),
            });
        });

        try
        {
            SegmentsDownloadResult results = await downloader.DownloadSegmentsAsync(request, progress);

            if (results.Segments.Count == 0)
            {
                throw new BadRequestException("No segments were generated.");
            }

            if (results.Segments.Count == 1)
            {
                string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{results.Segments[0].FileName}");
                await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    await results.Segments[0].FileStream.CopyToAsync(fileStream);
                }
                await results.Segments[0].FileStream.DisposeAsync();

                job.ResultFilePath = tempPath;
                job.ResultFileName = results.Segments[0].FileName;
                job.ResultContentType = request.Container?.ToLowerInvariant() switch
                {
                    "mp4" => "video/mp4",
                    "webm" => "video/webm",
                    _ => "application/octet-stream"
                };
            }
            else
            {
                string tempZipPath = await BuildZipFromSegmentsAsync(results.Segments, CancellationToken.None);
                job.ResultFilePath = tempZipPath;
                job.ResultFileName = $"{results.VideoTitle}.zip";
                job.ResultContentType = "application/zip";
            }

            job.Status = DownloadJobStatus.Completed;
            WriteEvent(job, new { status = "completed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Segment download job {JobId} failed", job.Id);
            job.Status = DownloadJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            WriteEvent(job, new { status = "failed", error = ex.Message });
        }
        finally
        {
            job.Events.Writer.TryComplete();
        }
    }

    private static void WriteEvent(DownloadJob job, object payload)
    {
        string json = JsonSerializer.Serialize(payload, JobEventJsonOptions);
        job.LastEventJson = json;
        job.Events.Writer.TryWrite(json);
    }

    /// <summary>
    /// Server-Sent Events stream of progress for a segment job. The client
    /// opens this with a plain EventSource — no extra libraries needed.
    /// </summary>
    [HttpGet("download-segments/{jobId}/events")]
    public Task StreamSegmentJobEvents(Guid jobId, CancellationToken cancellationToken) =>
        StreamJobEventsAsync(jobId, cancellationToken);

    /// <summary>
    /// Same SSE stream as above, for single full-video jobs. Kept as a
    /// separate route so the URL reflects which kind of download it is, but
    /// backed by the same implementation — a job is a job once it's running.
    /// </summary>
    [HttpGet("download/{jobId}/events")]
    public Task StreamVideoJobEvents(Guid jobId, CancellationToken cancellationToken) =>
        StreamJobEventsAsync(jobId, cancellationToken);

    private async Task StreamJobEventsAsync(Guid jobId, CancellationToken cancellationToken)
    {
        DownloadJob? job = _jobManager.Get(jobId);
        if (job == null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        // Replay whatever was last actually emitted, so a client that
        // subscribes late (or reconnects) isn't stuck at nothing until the
        // next event. Using the cached raw JSON (rather than reconstructing
        // it from job fields) keeps this endpoint agnostic to whether the
        // job is a segment job or a single-video job — they report different
        // shaped payloads.
        string replay = job.LastEventJson ?? job.Status switch
        {
            DownloadJobStatus.Completed => JsonSerializer.Serialize(new { status = "completed" }, JobEventJsonOptions),
            DownloadJobStatus.Failed => JsonSerializer.Serialize(new { status = "failed", error = job.ErrorMessage }, JobEventJsonOptions),
            _ => JsonSerializer.Serialize(new { status = "pending" }, JobEventJsonOptions),
        };
        await WriteSseLine(replay, cancellationToken);

        if (job.Status is DownloadJobStatus.Completed or DownloadJobStatus.Failed)
        {
            return;
        }

        try
        {
            await foreach (string message in job.Events.Reader.ReadAllAsync(cancellationToken))
            {
                await WriteSseLine(message, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected/navigated away — nothing to clean up here,
            // the background job keeps running regardless.
        }

        async Task WriteSseLine(string data, CancellationToken ct)
        {
            await Response.WriteAsync($"data: {data}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    /// <summary>
    /// Fetches the finished file once a segment job has completed.
    /// </summary>
    [HttpGet("download-segments/{jobId}/result")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status404NotFound)]
    public IActionResult GetSegmentJobResult(Guid jobId) => GetJobResultCore(jobId);

    /// <summary>
    /// Same result fetch as above, for single full-video jobs.
    /// </summary>
    [HttpGet("download/{jobId}/result")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status404NotFound)]
    public IActionResult GetVideoJobResult(Guid jobId) => GetJobResultCore(jobId);

    private IActionResult GetJobResultCore(Guid jobId)
    {
        DownloadJob? job = _jobManager.Get(jobId);
        if (job == null)
        {
            throw new NotFoundException("Job not found.");
        }

        if (job.Status == DownloadJobStatus.Failed)
        {
            _jobManager.Remove(jobId);
            throw new BadRequestException(job.ErrorMessage ?? "Download failed.");
        }

        if (job.Status != DownloadJobStatus.Completed || job.ResultFilePath == null)
        {
            throw new BadRequestException("Job is not ready yet.");
        }

        var readStream = new FileStream(
            job.ResultFilePath, FileMode.Open, FileAccess.Read, FileShare.None,
            4096, FileOptions.DeleteOnClose | FileOptions.Asynchronous);

        _jobManager.Remove(jobId);
        return File(readStream, job.ResultContentType ?? "application/octet-stream", job.ResultFileName ?? "download");
    }

}