using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using SegmentAPI.interfaces;
using SegmentAPI.Exceptions;
using SegmentAPI.Models;
namespace SegmentAPI.Constrollers;

[ApiController]
[Route("/api/v1")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class SegmentController : Controller
{
    private readonly IYoutubeDownloader _youtubeDownloader;
    private readonly IYoutubeSegmentDownloader _youtubeSegmentDownloader;

    public SegmentController(IYoutubeDownloader youtubeDownloader, IYoutubeSegmentDownloader youtubeSegmentDownloader)
    {
        this._youtubeDownloader = youtubeDownloader;
        this._youtubeSegmentDownloader = youtubeSegmentDownloader;
    }

    [Route("fetch")]
    [HttpPost]
    [ProducesResponseType(typeof(ResponseModel<YoutubeVideo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetVideoInfo([FromBody] FetchVideoRequest request)
    {
        try
        {
            YoutubeVideo? videoInfo = await _youtubeDownloader.GetVideoInfoAsync(request.YoutubeUrl);
            ResponseModel<YoutubeVideo> response = new ResponseModel<YoutubeVideo>
            {
                Data = videoInfo,
                Message = "Video was fetched successfully",
                StatusCode = StatusCodes.Status200OK,
            };

            return Ok(response);
        }
        catch (BadRequest ex)
        {
            string message = ex.Message ?? "";
            ResponseModel<object> error = new ResponseModel<object>
            {
                Data = null,
                Message = message,
                StatusCode = StatusCodes.Status500InternalServerError,
            };

            return StatusCode(StatusCodes.Status500InternalServerError, error);
        }
        catch (Exception ex)
        {
            string message = ex.InnerException?.Message ?? ex.Message ?? "Something went wrong please try again.";
            ResponseModel<object> error = new ResponseModel<object>
            {
                Data = null,
                Message = message,
                StatusCode = StatusCodes.Status500InternalServerError,
            };
            return StatusCode(StatusCodes.Status500InternalServerError, error);
        }
    }

    [HttpPost("download-segments")]
    [ProducesResponseType(typeof(ResponseModel<List<SegmentResult>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseModel<object>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadSegments(
        [FromBody] DownloadSegmentsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {

            DownloadSegmentsRequest segmentsRequest = new DownloadSegmentsRequest()
            {
                Url = request.Url,
                SelectedQuality = request.SelectedQuality,
                Segments = request.Segments,
                OutputDirectory = request.OutputDirectory,
            };

            List<SegmentResult>? results = await _youtubeSegmentDownloader.DownloadSegmentsAsync(segmentsRequest);

            ResponseModel<List<SegmentResult>> response = new ResponseModel<List<SegmentResult>>
            {
                Data = results,
                Message = "Segments downloaded successfully",
                StatusCode = StatusCodes.Status200OK,
            };

            return Ok(response);
        }
        catch (BadRequest ex)
        {
            ResponseModel<object> error = new ResponseModel<object>
            {
                Data = null,
                Message = ex.Message ?? "",
                StatusCode = StatusCodes.Status500InternalServerError,
            };

            return StatusCode(StatusCodes.Status500InternalServerError, error);
        }
        catch (Exception ex)
        {
            ResponseModel<object> error = new ResponseModel<object>
            {
                Data = null,
                Message = ex.InnerException?.Message ?? ex.Message ?? "Something went wrong please try again.",
                StatusCode = StatusCodes.Status500InternalServerError,
            };

            return StatusCode(StatusCodes.Status500InternalServerError, error);
        }
    }

}

