using System.Net;
using SegmentAPI.Models;
using YoutubeExplode.Videos.Streams;
namespace SegmentAPI.Extensions;

public static class Extensions
{
    public static IServiceCollection RegisterCors(this IServiceCollection services, string myAllowSpecificOrigins)
    {
        services.AddCors(options =>
        {
            options.AddPolicy(name: myAllowSpecificOrigins,
                policy =>
                {
                    policy.WithOrigins("http://localhost:5173")
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .WithExposedHeaders("Content-Disposition");

                });
        });

        return services;
    }

    public static IVideoStreamInfo? ProcessVideoStream(this StreamManifest streamManifest, string selectedQuality)
    {
        IVideoStreamInfo? videoStreamInfo = streamManifest
            .GetVideoStreams()
            .Where(s => s.VideoQuality.Label == selectedQuality)
            .OrderByDescending(s => s.Bitrate)
            .FirstOrDefault();

        return videoStreamInfo;
    }

    public static IAudioStreamInfo? ProcessAudioStream(this StreamManifest streamManifest)
    {
        IAudioStreamInfo? audioStreamInfo = streamManifest
            .GetAudioStreams()
            .OrderByDescending(s => s.Bitrate)
            .FirstOrDefault();

        return audioStreamInfo;
    }
}

