using SegmentAPI.Models;
namespace SegmentAPI.interfaces;

public interface IYoutubeDownloader
{
    Task<YoutubeVideo> GetVideoInfoAsync(string url);
    Task<DownloadResult> DownloadToWebStreamAsync(
    DownloadStreamRequest downloadStreamRequest,
    CancellationToken cancellationToken = default);
    Task<SegmentsDownloadResult> DownloadSegmentsAsync(DownloadSegmentsRequest request, CancellationToken cancellationToken = default);

}
