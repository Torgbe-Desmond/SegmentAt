namespace SegmentAPI.Exceptions;

/// <summary>
/// Base for all handled application errors. ExceptionMiddleware maps these
/// to the same HTTP status codes as Node's errorHandler.js.
/// </summary>
public class AppException : Exception
{
    public int StatusCode { get; }

    public AppException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}

public class NotFoundException : AppException
{
    public NotFoundException(string message = "Resource not found") : base(message, 404) { }
}

public class BadRequestException : AppException
{
    public BadRequestException(string message = "Resource not found") : base(message, 400) { }
}

public class ForbiddenException : AppException
{
    public ForbiddenException(string message = "Forbidden") : base(message, 403) { }
}

public class ConflictException : AppException
{
    public ConflictException(string message = "Conflict") : base(message, 409) { }
}

public class ValidationException : AppException
{
    public IDictionary<string, string> Errors { get; }

    public ValidationException(IDictionary<string, string> errors, string message = "Validation failed")
        : base(message, 422)
    {
        Errors = errors;
    }
}
