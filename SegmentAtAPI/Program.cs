using SegmentAPI.interfaces;
using SegmentAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// builder.Services.AddOpenApi();
builder.Services.AddScoped<IYoutubeDownloader, YoutubeDownloader>();
builder.Services.AddScoped<IYoutubeSegmentDownloader, YoutubeSegmentDownloader>();
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();

