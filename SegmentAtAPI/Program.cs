using System.Net;
using SegmentAPI.interfaces;
using SegmentAPI.Services;
using SegmentAPI.Extensions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string myAllowSpecificOrigins = "_myAllowSpecificOrigins";

builder.Services.AddHttpClient("YoutubeProxyClient")
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var proxyHost = builder.Configuration["Proxy:Host"];
        var proxyPort = builder.Configuration["Proxy:Port"];
        var proxyUser = builder.Configuration["Proxy:Username"];
        var proxyPass = builder.Configuration["Proxy:Password"];

        var handler = new HttpClientHandler();

        if (!string.IsNullOrEmpty(proxyHost))
        {
            handler.Proxy = new WebProxy($"http://{proxyHost}:{proxyPort}")
            {
                Credentials = new NetworkCredential(proxyUser, proxyPass)
            };
            handler.UseProxy = true;
        }

        return handler;
    });

builder.Services.AddScoped<IYoutubeDownloader, YoutubeDownloader>();
builder.Services.AddSingleton<JobManager>();
builder.Services.RegisterCors(myAllowSpecificOrigins);

builder.Services.AddControllers();

WebApplication app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(myAllowSpecificOrigins);

app.Run();