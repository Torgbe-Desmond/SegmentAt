using System.Diagnostics;
using YoutubeExplode;
using YoutubeExplode.Common;
using SegmentAPI.Models;
using SegmentAPI.interfaces;
using SegmentAPI.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace SegmentAPI.Services;

public partial class YoutubeSegmentDownloader : IYoutubeSegmentDownloader
{
    private readonly YoutubeClient _youtube = new();
    
    public async Task<List<SegmentResult>> DownloadSegmentsAsync(DownloadSegmentsRequest request, CancellationToken cancellationToken = default)
    {
        var results = new List<SegmentResult>();

        if (request.Segments.Count == 0)
        {
            throw new BadRequest("No segments provided.");
        }

        // Validate timestamps up front
        foreach (var seg in request.Segments)
        {
            if (seg.End <= seg.Start)
            {
                throw new BadRequest($"End time must be after start time for '{seg.Name}'.");
            }
        }

        var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(request.Url, cancellationToken);

        var videoStreamInfo = streamManifest
            .GetVideoStreams()
            .Where(s => s.VideoQuality.Label == request.SelectedQuality)
            .OrderByDescending(s => s.Bitrate)
            .FirstOrDefault();

        if (videoStreamInfo == null)
        {
            throw new NotFound($"Quality '{request.SelectedQuality}' not available.");
        }

        var audioStreamInfo = streamManifest
            .GetAudioStreams()
            .OrderByDescending(s => s.Bitrate)
            .FirstOrDefault();

        if (audioStreamInfo == null)
        {
            throw new NotFound("No suitable audio stream found.");
        }

        var video = await _youtube.Videos.GetAsync(request.Url, cancellationToken);
        Directory.CreateDirectory(request.OutputDirectory);

        foreach (var seg in request.Segments)
        {
            var safeName = SanitizeFileName(string.IsNullOrWhiteSpace(seg.Name) ? video.Title : seg.Name);
            var outputPath = Path.Combine(request.OutputDirectory, $"{safeName}.mp4");

            var cutResult = await CutSegmentFromStreamAsync(
                videoStreamInfo.Url,
                audioStreamInfo.Url,
                outputPath,
                seg.Start,
                seg.End,
                cancellationToken);

            results.Add(new SegmentResult
            {
                Name = seg.Name,
                Success = cutResult.success,
                OutputPath = cutResult.success ? outputPath : null,
                ErrorMessage = cutResult.error
            });
        }

        return results;
    }

    private async Task<SegmentStatusResult> CutSegmentFromStreamAsync(
        string videoUrl,
        string audioUrl,
        string outputPath,
        TimeSpan start,
        TimeSpan end,
        CancellationToken cancellationToken)
    {
        try
        {
            var duration = end - start;
            var startArg = start.ToString(@"hh\:mm\:ss\.fff");
            var durationArg = duration.ToString(@"hh\:mm\:ss\.fff");

            // -ss before each -i seeks at the input level (fast, uses HTTP range requests,
            // only pulls the bytes needed for this segment). Re-encoding (not -c copy)
            // trades some speed for frame-accurate cut points.
            var args =
                $"-ss {startArg} -i \"{videoUrl}\" " +
                $"-ss {startArg} -i \"{audioUrl}\" " +
                $"-map 0:v -map 1:a " +
                $"-t {durationArg} " +
                $"-c:v libx264 -c:a aac -preset veryfast -y \"{outputPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = args,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                throw new Exception("Failed to start ffmpeg process.");

            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
                throw new Exception($"ffmpeg failed: {stderr}");

            return new SegmentStatusResult(true, null);
        }
        catch (System.Exception)
        {

            throw;
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}



// var downloader = new YoutubeDownloader(maxHeight: 1080);

// var segments = new List<VideoSegment>
// {
//     new() { Name = "Intro", Start = TimeSpan.Zero, End = TimeSpan.FromMinutes(7) },
//     new() { Name = "Highlight", Start = TimeSpan.FromMinutes(12), End = TimeSpan.FromMinutes(15) }
// };

// var results = await downloader.DownloadSegmentsAsync(
//     "https://youtube.com/watch?v=u_yIGGhubZs",
//     "1080p",
//     segments,
//     "./downloads"
// );

// foreach (var r in results)
//     Console.WriteLine(r.Success ? $"{r.Name} -> {r.OutputPath}" : $"{r.Name} failed: {r.ErrorMessage}");