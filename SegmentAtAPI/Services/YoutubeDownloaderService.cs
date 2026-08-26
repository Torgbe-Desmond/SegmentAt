using YoutubeExplode;
using YoutubeExplode.Common;
using SegmentAPI.Models;
using YoutubeExplode.Videos.Streams;
using YoutubeExplode.Converter;
using SegmentAPI.interfaces;
using SegmentAPI.Exceptions;
using System.Diagnostics;
using SegmentAPI.Extensions;
using YoutubeExplode.Videos;
using System.Net;

namespace SegmentAPI.Services;

public class YoutubeDownloader : IYoutubeDownloader
{
    private readonly YoutubeClient _youtube;
    private readonly int _maxHeight;

    public YoutubeDownloader(IConfiguration configuration, int maxHeight = 1080)
    {
        _maxHeight = maxHeight;

        string? cookiesPath = configuration["Youtube:CookiesPath"];

        if (string.IsNullOrWhiteSpace(cookiesPath))
        {
            Console.WriteLine("[YoutubeDownloader] No Youtube:CookiesPath configured — running unauthenticated.");
            _youtube = new YoutubeClient();
        }
        else if (!File.Exists(cookiesPath))
        {
            Console.WriteLine($"[YoutubeDownloader] Cookie file not found at '{Path.GetFullPath(cookiesPath)}' — running unauthenticated.");
            _youtube = new YoutubeClient();
        }
        else
        {
            List<Cookie> cookies = CookieLoader.LoadFromNetscapeFile(cookiesPath);
            Console.WriteLine($"[YoutubeDownloader] Loaded {cookies.Count} cookies from '{cookiesPath}'.");
            _youtube = new YoutubeClient(cookies);
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    public static string? GetVideoId(string youtubeUrl)
    {
        if (string.IsNullOrWhiteSpace(youtubeUrl))
        {
            return null;
        }

        return VideoId.TryParse(youtubeUrl);
    }

    public async Task<YoutubeVideo> GetVideoInfoAsync(string url)
    {
        try
        {

            if (string.IsNullOrWhiteSpace(url)) throw new BadRequestException($"Video url cannot be empty");

            string? videoId = GetVideoId(url);

            if (videoId == null) throw new BadRequestException("Could not parse videoId");

            YoutubeExplode.Videos.Video? video;

            try
            {
                video = await _youtube.Videos.GetAsync(videoId);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                throw;
            }

            StreamManifest streamManifest = await _youtube.Videos.Streams.GetManifestAsync(videoId);

            string thumbnail = video.Thumbnails.GetWithHighestResolution()?.Url ?? "";

            List<VideoQualityOption> qualityOptions = streamManifest
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

    public async Task<DownloadResult> DownloadToWebStreamAsync(
    DownloadStreamRequest downloadStreamRequest,
    CancellationToken cancellationToken = default)
    {
        StreamManifest? streamManifest = await _youtube.Videos.Streams.GetManifestAsync(downloadStreamRequest.Url, cancellationToken);

        IVideoStreamInfo? videoStreamInfo = streamManifest.ProcessVideoStream(downloadStreamRequest.SelectedQuality);

        if (videoStreamInfo == null)
            throw new NotFoundException($"Selected quality '{downloadStreamRequest.SelectedQuality}' is no longer available.");

        IAudioStreamInfo? audioStreamInfo = streamManifest.ProcessAudioStream();

        if (audioStreamInfo == null)
            throw new NotFoundException("No suitable audio stream found.");

        ProcessVideoRequest videoRequest = new ProcessVideoRequest
        {
            Url = downloadStreamRequest.Url,
            Container = downloadStreamRequest.Container,
            VideoStreamInfo = videoStreamInfo,
            AudioStreamInfo = audioStreamInfo
        };

        return await ProcessVideo(videoRequest, cancellationToken);
    }


    public async Task<SegmentsDownloadResult> DownloadSegmentsAsync(DownloadSegmentsRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Segments.Count == 0)
        {
            throw new BadRequestException("No segments provided.");
        }

        foreach (VideoSegment seg in request.Segments)
        {
            if (seg.End <= seg.Start)
            {
                throw new BadRequestException($"End time must be after start time for '{seg.Name}'.");
            }
        }

        StreamManifest streamManifest = await _youtube.Videos.Streams.GetManifestAsync(request.Url, cancellationToken);

        IVideoStreamInfo? videoStreamInfo = streamManifest.ProcessVideoStream(request.SelectedQuality);

        if (videoStreamInfo == null)
        {
            throw new NotFoundException($"Quality '{request.SelectedQuality}' not available.");
        }

        IAudioStreamInfo? audioStreamInfo = streamManifest.ProcessAudioStream();

        if (audioStreamInfo == null)
        {
            throw new NotFoundException("No suitable audio stream found.");
        }

        YoutubeExplode.Videos.Video video = await _youtube.Videos.GetAsync(request.Url, cancellationToken);

        List<DownloadResult> downloadResult = await ProcessVideoList(audioStreamInfo, videoStreamInfo, request, video, cancellationToken);

        return new SegmentsDownloadResult
        {
            Segments = downloadResult,
            VideoTitle = SanitizeFileName(video.Title)
        };

    }

    private async Task<SegmentStatusResult> CutSegmentFromStreamAsync(
       string videoUrl,
       string audioUrl,
       long videoBitrateBps,
       string outputPath,
       TimeSpan start,
       TimeSpan end,
       CancellationToken cancellationToken)
    {
        try
        {
            TimeSpan duration = end - start;
            string startArg = start.ToString(@"hh\:mm\:ss\.fff");
            string durationArg = duration.ToString(@"hh\:mm\:ss\.fff");

            string args =
                $"-ss {startArg} -i \"{videoUrl}\" " +
                $"-ss {startArg} -i \"{audioUrl}\" " +
                $"-map 0:v -map 1:a " +
                $"-t {durationArg} " +
                $"-c:v libx264 -b:v {videoBitrateBps} -maxrate {videoBitrateBps} -bufsize {videoBitrateBps * 2} " +
                $"-c:a aac -b:a 128k -preset veryfast -y \"{outputPath}\"";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = args,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process? process = Process.Start(psi);
            if (process == null)
                throw new BadRequestException("Failed to start ffmpeg process.");

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                string stderr = await stderrTask;
                throw new BadRequestException($"ffmpeg failed: {stderr}");
            }

            return new SegmentStatusResult(true, null);
        }
        catch (Exception)
        {
            // Fix: clean up whatever ffmpeg may have partially written before
            // rethrowing, so failed cuts don't leak files into the temp folder.
            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
            catch
            {
                // Best-effort cleanup; the original exception is what matters.
            }

            throw;
        }
    }

    private async Task<DownloadResult> ProcessVideo(ProcessVideoRequest videoRequest, CancellationToken ct = default)
    {
        YoutubeExplode.Videos.Video video = await _youtube.Videos.GetAsync(videoRequest.Url, ct);

        string fileName = $"{SanitizeFileName(video.Title)}.{videoRequest.Container}";
        // Fix: fileName already carries the container extension, so appending
        // ".{Container}" again here produced a double-extension path
        // (e.g. "video.mp4.mp4").
        string tempPath = Path.Combine(Path.GetTempPath(), fileName);

        await _youtube.Videos.DownloadAsync(
            new IStreamInfo[] { videoRequest.VideoStreamInfo, videoRequest.AudioStreamInfo },
            new ConversionRequestBuilder(tempPath).Build(),
            null,
            ct
        );

        Stream tempFileStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.DeleteOnClose);

        return new DownloadResult
        {
            FileStream = tempFileStream,
            FileName = fileName
        };
    }

    private async Task<List<DownloadResult>> ProcessVideoList(
        IAudioStreamInfo audioStreamInfo,
        IVideoStreamInfo videoStreamInfo,
        DownloadSegmentsRequest downloadSegmentsRequest,
        YoutubeExplode.Videos.Video video,
        CancellationToken ct = default
        )
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<DownloadResult> results = new List<DownloadResult>();

        try
        {
            foreach (VideoSegment seg in downloadSegmentsRequest.Segments)
            {
                string baseName = SanitizeFileName(string.IsNullOrWhiteSpace(seg.Name) ? video.Title : seg.Name);
                string safeName = baseName;
                int suffix = 1;
                while (!usedNames.Add(safeName))
                {
                    safeName = $"{baseName} ({suffix++})";
                }

                string fileName = $"{safeName}.{downloadSegmentsRequest.Container}";
                string tempPath = Path.Combine(Path.GetTempPath(), fileName);

                SegmentStatusResult cutResult = await CutSegmentFromStreamAsync(
                    videoStreamInfo.Url,
                    audioStreamInfo.Url,
                    videoStreamInfo.Bitrate.BitsPerSecond,
                    tempPath,
                    seg.Start,
                    seg.End,
                    ct);

                Stream tempFileStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.DeleteOnClose);

                results.Add(new DownloadResult
                {
                    FileStream = tempFileStream,
                    FileName = fileName
                });
            }

            return results;
        }
        catch
        {
            foreach (var result in results)
            {
                await result.FileStream.DisposeAsync();
            }
            throw;
        }

    }
}