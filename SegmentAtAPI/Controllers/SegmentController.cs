using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using SegmentAPI.interfaces;
using SegmentAPI.Exceptions;
using SegmentAPI.Models;
using System.IO.Compression;
namespace SegmentAPI.Constrollers;

[ApiController]
[Route("/api/v1")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class SegmentController : Controller
{
    private readonly IYoutubeDownloader _youtubeDownloader;

    public SegmentController(IYoutubeDownloader youtubeDownloader)
    {
        this._youtubeDownloader = youtubeDownloader;
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
            DownloadResult downloadResult = await _youtubeDownloader.DownloadToWebStreamAsync(request, cancellationToken);

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
            results = await _youtubeDownloader.DownloadSegmentsAsync(request, cancellationToken);

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

        Response.ContentType = "application/zip";
        Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{zipFileName}\"");

        try
        {
            using (var archive = new ZipArchive(Response.Body, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var result in results.Segments)
                {
                    var zipEntry = archive.CreateEntry(result.FileName, CompressionLevel.Fastest);

                    using (var entryStream = zipEntry.Open())
                    {
                        await result.FileStream.CopyToAsync(entryStream, cancellationToken);
                    }

                    await result.FileStream.DisposeAsync();
                }
            }
        }
        catch (Exception)
        {
            foreach (var result in results.Segments)
            {
                try { await result.FileStream.DisposeAsync(); } catch { }
            }
        }

        return new EmptyResult();
    }

}