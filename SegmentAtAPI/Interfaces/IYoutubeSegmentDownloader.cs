using Microsoft.AspNetCore.Mvc;
using SegmentAPI.Models;
namespace SegmentAPI.interfaces;

public interface IYoutubeSegmentDownloader
{
    // Task<List<SegmentResult>> DownloadSegmentsAsync(
    //         string url,
    //         string selectedQuality,
    //         List<VideoSegment> segments,
    //         string outputDirectory,
    //         CancellationToken cancellationToken = default);

    Task<List<SegmentResult>> DownloadSegmentsAsync(DownloadSegmentsRequest request, CancellationToken cancellationToken = default);
}