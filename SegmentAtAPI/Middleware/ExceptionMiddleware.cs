

using System.Text.Json;
using SegmentAPI.Exceptions;
using SegmentAPI.Models;

public class ExceptionMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException vex)
        {
            await WriteAsync(context, vex.StatusCode, new { message = vex.Message, errors = vex.Errors });
        }
        catch (AppException aex)
        {
            await WriteAsync(context, aex.StatusCode, new { message = aex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Path}", context.Request.Path);
            await WriteAsync(context, 500, new { message = "Internal server error" });
        }
    }

    private static Task WriteAsync(HttpContext context, int statusCode, object payload)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        ResponseModel<object> responseModel = new ResponseModel<object>
        {
            Data = payload,
            Message = "Something went wrong please try again",
            StatusCode = statusCode,
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(responseModel, JsonOptions));
    }
}
