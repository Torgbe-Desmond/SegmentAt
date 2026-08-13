using YoutubeExplode;
using YoutubeExplode.Common;
using SegmentAPI.Models;
using YoutubeExplode.Videos.Streams;
using YoutubeExplode.Converter;
using SegmentAPI.interfaces;
using SegmentAPI.Exceptions;

namespace SegmentAPI.Services;

public class YoutubeDownloader : IYoutubeDownloader
{
    private readonly YoutubeClient _youtube = new();
    private readonly int _maxHeight;

    public YoutubeDownloader(int maxHeight = 1080)
    {
        _maxHeight = maxHeight;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private static YoutubeDownloadResult Fail(string message) => new()
    {
        Success = false,
        ErrorMessage = message
    };

    public async Task<YoutubeVideo> GetVideoInfoAsync(string url)
    {

        try
        {

            if(string.IsNullOrWhiteSpace(url)) throw new BadRequest($"Video url cannot be empty");

            var video = await _youtube.Videos.GetAsync(url);
            var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(url);

            var thumbnail = video.Thumbnails.GetWithHighestResolution()?.Url ?? "";

            var qualityOptions = streamManifest
                .GetVideoStreams()
                .Where(s => s.VideoQuality.MaxHeight <= _maxHeight)
                .GroupBy(s => s.VideoQuality.Label)
                .Select(g => g.OrderByDescending(s => s.Bitrate).First()) 
                .OrderByDescending(s => s.VideoQuality.MaxHeight)
                .Select(s => new VideoQualityOption
                {
                    Quality = s.VideoQuality.Label,
                    Container = s.Container.Name,
                    BitrateBps = s.Bitrate.BitsPerSecond,
                    Thumbnail = thumbnail
                })
                .ToList();

            return new YoutubeVideo
            {
                Title = video.Title,
                Thumbnail = thumbnail,
                Url = url,
                ChannelTitle = video.Author.ChannelTitle,
                Duration = video.Duration?.ToString(@"hh\:mm\:ss") ?? "Unknown",
                QualityOptions = qualityOptions
            };
        }
        catch (Exception)
        {
            throw;
        }

    }

    public async Task<YoutubeDownloadResult> DownloadAsync(
    string url,
    string selectedQuality,
    string outputDirectory,
    IProgress<double>? progress = null,
    CancellationToken cancellationToken = default)
    {
        try
        {
            var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(url, cancellationToken);

            var videoStreamInfo = streamManifest
                .GetVideoStreams()
                .Where(s => s.VideoQuality.Label == selectedQuality)
                .OrderByDescending(s => s.Bitrate)
                .FirstOrDefault();

            if (videoStreamInfo == null)
                return Fail($"Selected quality '{selectedQuality}' is no longer available.");

            var audioStreamInfo = streamManifest
                .GetAudioStreams()
                .OrderByDescending(s => s.Bitrate)
                .FirstOrDefault();

            if (audioStreamInfo == null)
                return Fail("No suitable audio stream found.");

            var video = await _youtube.Videos.GetAsync(url, cancellationToken);
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, SanitizeFileName(video.Title) + ".mp4");

            await _youtube.Videos.DownloadAsync(
                new IStreamInfo[] { videoStreamInfo, audioStreamInfo },
                new ConversionRequestBuilder(outputPath).Build(),
                progress,
                cancellationToken
            );

            return new YoutubeDownloadResult
            {
                Success = true,
                OutputPath = outputPath,
                SelectedVideoQuality = videoStreamInfo.VideoQuality.Label,
                SelectedVideoContainer = videoStreamInfo.Container.Name,
                SelectedAudioBitrate = audioStreamInfo.Bitrate.BitsPerSecond
            };
        }
        catch (OperationCanceledException ex)
        { 

            string message = ex.InnerException?.Message?? ex.Message;
            throw new BadRequest(message);
        }
        catch (Exception ex)
        {
            return Fail($"Download failed: {ex.Message}");
        }
    }

}