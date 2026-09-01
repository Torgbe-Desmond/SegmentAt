using SegmentAPI.Models;
namespace SegmentAPI.interfaces;

public interface IYoutubeDownloader
{
    Task<YoutubeVideo> GetVideoInfoAsync(string url);

    Task<DownloadResult> DownloadToWebStreamAsync(
        DownloadStreamRequest downloadStreamRequest,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Task<SegmentsDownloadResult> DownloadSegmentsAsync(
        DownloadSegmentsRequest request,
        IProgress<SegmentProgress>? progress = null,
        CancellationToken cancellationToken = default);
}