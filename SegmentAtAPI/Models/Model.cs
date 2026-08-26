using System.ComponentModel.DataAnnotations;
using YoutubeExplode.Videos.Streams;
namespace SegmentAPI.Models;

public class YoutubeVideo
{
    [Key]
    public Guid VideoId { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Thumbnail { get; set; } = "";
    public string Url { get; set; } = "";
    public string ChannelTitle { get; set; } = "";
    public string Duration { get; set; } = "";
    public List<VideoQualityOption> QualityOptions { get; set; } = new();
}

public class VideoQualityOption
{
    public string Quality { get; set; } = "";
    public string Container { get; set; } = "";
    public long BitrateBps { get; set; }
    public string Thumbnail { get; set; } = "";
}

public class DownloadResult
{
    public required Stream FileStream { get; set; }
    public string FileName { get; set; } = string.Empty;
}

public class SegmentsDownloadResult
{
    public List<DownloadResult> Segments { get; set; } = new();
    public string VideoTitle { get; set; } = string.Empty;

    // public virtual DownloadResult downloadResult {get;set; }
}

public class VideoSegment
{
    public string Name { get; set; } = "";
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }

}

public record SegmentStatusResult(bool success, string? error);

public class ResponseModel<T> where T : class
{
    public T? Data { get; set; }
    public string Message { get; set; } = string.Empty;
    public int StatusCode { get; set; }

}

public class FetchVideoRequest
{
    public string YoutubeUrl { get; set; } = string.Empty;
}

public class DownloadSegmentStreamRequest
{
    public string Url { get; set; } = string.Empty;

    public string SelectedQuality { get; set; } = string.Empty;

    public string Container { get; set; } = string.Empty;

    public List<VideoSegment> Segments { get; set; } = new();

}

public class DownloadStreamRequest
{
    public string Url { get; set; } = string.Empty;

    public string SelectedQuality { get; set; } = string.Empty;

    public string Container { get; set; } = string.Empty;
}

public class DownloadSegmentsRequest
{
    public string Url { get; set; } = string.Empty;

    public string SelectedQuality { get; set; } = string.Empty;

    public string Container { get; set; } = string.Empty;

    public List<VideoSegment> Segments { get; set; } = new();
}

public class ProcessVideoRequest
{
    public string Url { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;
    public required IVideoStreamInfo VideoStreamInfo { get; set; }
    public required IAudioStreamInfo AudioStreamInfo { get; set; }
}
