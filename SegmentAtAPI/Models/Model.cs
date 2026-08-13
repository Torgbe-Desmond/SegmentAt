namespace SegmentAPI.Models;

public class YoutubeVideo
{
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

public class YoutubeDownloadResult
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? SelectedVideoQuality { get; set; }
    public string? SelectedVideoContainer { get; set; }
    public long? SelectedAudioBitrate { get; set; }
    public string? ErrorMessage { get; set; }
}


// Video Segmentation 

public class VideoSegment
{
    public string Name { get; set; } = "";
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
}

public class SegmentResult
{
    public string Name { get; set; } = "";
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? ErrorMessage { get; set; }
}

public record SegmentStatusResult(bool success, string? error);

public class ResponseModel<T>
{
    public T? Data { get; set; }
    public string Message { get; set; } = string.Empty;
    public int StatusCode { get; set; }

}

public class FetchVideoRequest
{
    public string YoutubeUrl { get; set; } = string.Empty;
}

public class DownloadSegmentsRequest
{
    public string Url { get; set; } = string.Empty;

    public string SelectedQuality { get; set; } = string.Empty;

    public List<VideoSegment> Segments { get; set; } = new();

    public string OutputDirectory { get; set; } = string.Empty;
}