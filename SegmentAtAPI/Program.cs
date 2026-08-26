using SegmentAPI.interfaces;
using SegmentAPI.Services;
using SegmentAPI.Extensions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string myAllowSpecificOrigins = "_myAllowSpecificOrigins";

builder.Services.AddScoped<IYoutubeDownloader, YoutubeDownloader>();
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

