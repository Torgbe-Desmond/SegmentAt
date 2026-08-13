using SegmentAPI.Models;
namespace SegmentAPI.interfaces;

public interface IYoutubeDownloader
{
    Task<YoutubeVideo> GetVideoInfoAsync(string url);
    Task<YoutubeDownloadResult> DownloadAsync(
   string url,
   string selectedQuality,
   string outputDirectory,
   IProgress<double>? progress = null,
   CancellationToken cancellationToken = default);
}