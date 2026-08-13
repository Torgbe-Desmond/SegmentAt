namespace SegmentAPI.Exceptions;

public class BadRequest : Exception
{
    public int StatusCode { get; private set;}
    public BadRequest(string message) : base(message)
    {
        this.StatusCode = StatusCodes.Status400BadRequest;
    }

}

public class NotFound : Exception
{
    public int StatusCode { get; private set;}
    public NotFound(string message) : base(message)
    {
        this.StatusCode = StatusCodes.Status404NotFound;
    }

}